using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using TranslationApp.Models;
using TranslationApp.Services.Logging;
using TranslationApp.Services.Translation;

namespace TranslationApp.Views;

public partial class GoogleTranslateHostWindow : Window, IGoogleTranslateWebClient, IDisposable
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(200);
    private const int BrowserViewportWidth = 1024;
    private const int BrowserViewportHeight = 768;
    private readonly WebView2RuntimeInstaller _runtimeInstaller;
    private readonly AppLogger _logger;
    private readonly CancellationTokenSource _lifetime;
    private readonly string _userDataFolder;
    private readonly object _initializationGate = new();
    private Task? _initializationTask;
    private long _generation;
    private bool _disposed;

    public GoogleTranslateHostWindow(
        WebView2RuntimeInstaller runtimeInstaller,
        AppLogger logger,
        CancellationToken applicationLifetime,
        string? userDataFolder = null)
    {
        _runtimeInstaller = runtimeInstaller;
        _logger = logger;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(applicationLifetime);
        _userDataFolder = userDataFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TranslationApp",
            "WebView2");
        InitializeComponent();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Task initialization;
        lock (_initializationGate)
            initialization = _initializationTask ??= InitializeCoreAsync();

        try { await initialization.WaitAsync(cancellationToken); }
        catch
        {
            lock (_initializationGate)
            {
                if (ReferenceEquals(_initializationTask, initialization)) _initializationTask = null;
            }
            throw;
        }
    }

    Task<GoogleTranslateDomResult> IGoogleTranslateWebClient.TranslateOnceAsync(
        TranslationRequest request,
        Uri address,
        int attempt,
        CancellationToken cancellationToken) =>
        RunOnUiAsync(() => TranslateOnceOnUiAsync(request, address, attempt, cancellationToken), cancellationToken);

    private async Task InitializeCoreAsync()
    {
        if (!Dispatcher.CheckAccess())
        {
            var dispatched = await Dispatcher.InvokeAsync(InitializeCoreAsync).Task.WaitAsync(_lifetime.Token);
            await dispatched.WaitAsync(_lifetime.Token);
            return;
        }

        await _runtimeInstaller.EnsureInstalledAsync(_lifetime.Token);
        Directory.CreateDirectory(_userDataFolder);
        var environment = await CoreWebView2Environment.CreateAsync(null, _userDataFolder);
        await Browser.EnsureCoreWebView2Async(environment);
        ConfigureBrowser(Browser.CoreWebView2);
    }

    private void ConfigureBrowser(CoreWebView2 core)
    {
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsSwipeNavigationEnabled = false;
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
        core.DownloadStarting += (_, args) => args.Cancel = true;
        core.NavigationStarting += (_, args) =>
        {
            if (!IsAllowedTopLevelAddress(args.Uri)) args.Cancel = true;
        };
    }

    private async Task<GoogleTranslateDomResult> TranslateOnceOnUiAsync(
        TranslationRequest request,
        Uri address,
        int attempt,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        var core = Browser.CoreWebView2
            ?? throw new InvalidOperationException("WebView2 is not initialized.");
        var generation = Interlocked.Increment(ref _generation);
        _logger.Info($"DOM polling start request={request.RequestId:N}, generation={generation}, attempt={attempt}");

        await WaitForNavigationAsync(core, address, generation, attempt > 1, cancellationToken);
        var script = GoogleTranslateDomExtractor.BuildScript(
            request.OriginalText,
            GoogleTranslateLanguageCodes.GetCode(request.TargetLanguage));
        using var timer = new PeriodicTimer(PollingInterval);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureCurrentGeneration(generation);
                var json = await core.ExecuteScriptAsync(script).WaitAsync(cancellationToken);
                EnsureCurrentGeneration(generation);
                var result = GoogleTranslateDomExtractor.Decode(json);
                if (result is not null) return result;
                if (!await timer.WaitForNextTickAsync(cancellationToken))
                    throw new OperationCanceledException(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested)
        {
            await LogDomDiagnosticsAsync(core, request.RequestId, generation);
            throw;
        }
    }

    private async Task LogDomDiagnosticsAsync(CoreWebView2 core, Guid requestId, long generation)
    {
        try
        {
            if (generation != Volatile.Read(ref _generation)) return;
            var json = await core.ExecuteScriptAsync(GoogleTranslateDomExtractor.DiagnosticScript)
                .WaitAsync(TimeSpan.FromSeconds(1));
            _logger.Info($"Google DOM diagnostic request={requestId:N}, generation={generation}, state={json}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Google DOM diagnostic failed request={requestId:N}", ex);
        }
    }

    private async Task WaitForNavigationAsync(
        CoreWebView2 core,
        Uri address,
        long generation,
        bool reloadIfPossible,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ulong navigationId = 0;

        void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
        {
            if (generation == Volatile.Read(ref _generation) && navigationId == 0)
                navigationId = args.NavigationId;
        }

        void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (generation == Volatile.Read(ref _generation) && navigationId != 0 && args.NavigationId == navigationId)
                completion.TrySetResult(args);
        }

        core.NavigationStarting += OnNavigationStarting;
        core.NavigationCompleted += OnNavigationCompleted;
        using var cancellationRegistration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));
        try
        {
            if (reloadIfPossible && IsSameTranslationAddress(core.Source, address)) core.Reload();
            else core.Navigate(address.AbsoluteUri);

            var result = await completion.Task;
            EnsureCurrentGeneration(generation);
            if (!result.IsSuccess)
                throw new InvalidOperationException($"Google Translate navigation failed: {result.WebErrorStatus}");
            _logger.Info($"Google navigation completed generation={generation}, navigation={result.NavigationId}");
        }
        finally
        {
            core.NavigationStarting -= OnNavigationStarting;
            core.NavigationCompleted -= OnNavigationCompleted;
            if (cancellationToken.IsCancellationRequested && generation == Volatile.Read(ref _generation))
            {
                try { core.Stop(); } catch { }
            }
        }
    }

    private void EnsureCurrentGeneration(long generation)
    {
        if (generation != Volatile.Read(ref _generation))
            throw new OperationCanceledException("A newer translation navigation has replaced this request.");
    }

    private static bool IsAllowedTopLevelAddress(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var address)
        && string.Equals(address.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && string.Equals(address.Host, "translate.google.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsSameTranslationAddress(string? current, Uri expected) =>
        Uri.TryCreate(current, UriKind.Absolute, out var currentAddress)
        && string.Equals(currentAddress.Host, expected.Host, StringComparison.OrdinalIgnoreCase)
        && string.Equals(currentAddress.AbsolutePath, expected.AbsolutePath, StringComparison.Ordinal)
        && string.Equals(currentAddress.Query, expected.Query, StringComparison.Ordinal);

    private async Task<T> RunOnUiAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        if (Dispatcher.CheckAccess()) return await action();
        var dispatched = await Dispatcher.InvokeAsync(action).Task.WaitAsync(cancellationToken);
        return await dispatched.WaitAsync(cancellationToken);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExNoActivate | WsExToolWindow));
        SetWindowPos(
            handle,
            HwndBottom,
            -32000,
            -32000,
            BrowserViewportWidth,
            BrowserViewportHeight,
            SwpNoActivate | SwpNoZOrder);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Interlocked.Increment(ref _generation);
        _lifetime.Cancel();
        try { Browser.Dispose(); } catch { }
        _lifetime.Dispose();
        if (IsLoaded) Close();
    }

    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private static readonly IntPtr HwndBottom = new(1);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
