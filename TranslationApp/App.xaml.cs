using System.Windows;
using TranslationApp.Services;
using TranslationApp.Services.Clipboard;
using TranslationApp.Services.Hotkeys;
using TranslationApp.Services.Logging;
using TranslationApp.Services.Notifications;
using TranslationApp.Services.Ocr;
using TranslationApp.Services.Startup;
using TranslationApp.Services.Translation;
using TranslationApp.Services.Tray;
using TranslationApp.Services.UiAutomation;
using TranslationApp.Services.Windows;
using TranslationApp.Views;

namespace TranslationApp;

public partial class App : System.Windows.Application
{
    private readonly CancellationTokenSource _lifetime = new();
    private AppLogger? _logger;
    private HotkeyManager? _hotkeys;
    private WebView2RuntimeInstaller? _webViewRuntime;
    private GoogleTranslateHostWindow? _googleHost;
    private GoogleTranslateService? _googleTranslate;
    private TranslationRequestQueue? _queue;
    private TrayIconService? _tray;
    private TranslationWorkflowService? _workflow;
    private SettingsService? _settingsService;
    private Settings.AppSettings? _settings;
    private StartupManager? _startup;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var smokeTest = e.Args.Any(argument => string.Equals(argument, "--smoke-test", StringComparison.OrdinalIgnoreCase));
        _logger = new AppLogger();
        _settingsService = new SettingsService();
        _settings = _settingsService.Load();
        _startup = new StartupManager();
        var notifications = new NotificationService();
        var windows = new WindowRegistry();
        _webViewRuntime = new WebView2RuntimeInstaller(_logger);
        _googleHost = new GoogleTranslateHostWindow(_webViewRuntime, _logger, _lifetime.Token);
        _googleHost.Show();
        _googleTranslate = new GoogleTranslateService(_googleHost, _logger);
        _queue = new TranslationRequestQueue(_googleTranslate, _logger);
        _workflow = new TranslationWorkflowService(
            new ClipboardTextReader(new WindowsClipboardFacade()),
            _queue,
            new TesseractOcrService(_settings), new ScreenCaptureService(),
            new SelectionBoundsService(_logger), windows, new TranslationWindowPresenter(windows),
            notifications, _logger);

        _hotkeys = new HotkeyManager();
        _hotkeys.Pressed += (_, action) => _ = _workflow.HandleHotkeyAsync(action);
        try { _hotkeys.RegisterDefaults(); }
        catch (Exception ex)
        {
            _logger.Error("전역 단축키 등록 실패", ex);
            notifications.Show(ex.Message, true);
        }

        _tray = new TrayIconService(_googleTranslate, OpenSettings,
            () => _ = _workflow.HandleHotkeyAsync(HotkeyAction.CaptureOcr), Shutdown);
        _ = InitializeTranslationEngineAsync(notifications, smokeTest);
        _logger.Info("TranslationApp 시작");
    }

    private async Task InitializeTranslationEngineAsync(NotificationService notifications, bool shutdownWhenReady)
    {
        var exitCode = 0;
        try { await _googleTranslate!.InitializeAsync(_lifetime.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            exitCode = 1;
            notifications.Show(ex.Message, true);
        }
        finally
        {
            if (shutdownWhenReady) Shutdown(exitCode);
        }
    }

    private void OpenSettings()
    {
        if (_settings is null || _settingsService is null || _startup is null) return;
        var window = new SettingsWindow(_settings, _settingsService, _startup);
        window.ShowDialog();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _lifetime.Cancel();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _queue?.Dispose();
        _googleHost?.Dispose();
        _webViewRuntime?.Dispose();
        _lifetime.Dispose();
        _logger?.Info("TranslationApp 종료");
        base.OnExit(e);
    }
}
