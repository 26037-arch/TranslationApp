using System.Diagnostics;
using TranslationApp.Models;
using TranslationApp.Services.Logging;

namespace TranslationApp.Services.Translation;

public sealed class GoogleTranslateService : ITranslator, ITranslationEngineLifecycle
{
    public const int MaximumAttempts = 2;
    public static readonly TimeSpan TimeoutPerAttempt = TimeSpan.FromSeconds(15);

    private readonly IGoogleTranslateWebClient _webClient;
    private readonly AppLogger _logger;
    private readonly TimeSpan _attemptTimeout;
    private readonly int _maximumAttempts;
    private readonly object _initializationGate = new();
    private Task? _initializationTask;
    private TranslationEngineState _state;
    private string _statusMessage = "시작 전";

    internal GoogleTranslateService(
        IGoogleTranslateWebClient webClient,
        AppLogger logger,
        TimeSpan? attemptTimeout = null,
        int maximumAttempts = MaximumAttempts)
    {
        if (maximumAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        _webClient = webClient;
        _logger = logger;
        _attemptTimeout = attemptTimeout ?? TimeoutPerAttempt;
        _maximumAttempts = maximumAttempts;
    }

    public TranslationEngineState State => _state;
    public string StatusMessage => _statusMessage;
    public event EventHandler? StateChanged;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
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

    public async Task<TranslationResult> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var address = GoogleTranslateUrlBuilder.Build(request);
        await InitializeAsync(cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        Exception? lastFailure = null;

        for (var attempt = 1; attempt <= _maximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.Info($"Google navigation start request={request.RequestId:N}, attempt={attempt}");
            using var attemptLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptLifetime.CancelAfter(_attemptTimeout);
            try
            {
                var domResult = await _webClient.TranslateOnceAsync(
                    request,
                    address,
                    attempt,
                    attemptLifetime.Token);
                _logger.Info($"Google selector success request={request.RequestId:N}, selector={domResult.Selector}");
                return new TranslationResult(
                    request.RequestId,
                    request.OriginalText,
                    request.SourceLanguage,
                    request.TargetLanguage,
                    [new TranslationCandidate(domResult.Text)],
                    stopwatch.Elapsed);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastFailure = new TimeoutException(
                    $"Google Translate에서 {_attemptTimeout.TotalSeconds:0.#}초 안에 결과를 받지 못했습니다.");
                _logger.Error($"Google polling timeout request={request.RequestId:N}, attempt={attempt}", lastFailure);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                lastFailure = ex;
                _logger.Error($"Google translation attempt failed request={request.RequestId:N}, attempt={attempt}", ex);
            }

            if (attempt < _maximumAttempts)
                _logger.Info($"Google translation retry request={request.RequestId:N}, nextAttempt={attempt + 1}");
        }

        throw new InvalidOperationException(
            "Google Translate 번역에 실패했습니다. 인터넷 연결과 Google Translate 페이지 상태를 확인하세요.",
            lastFailure);
    }

    private async Task InitializeCoreAsync()
    {
        SetState(TranslationEngineState.Loading, "WebView2와 Google Translate를 준비하는 중…");
        try
        {
            await _webClient.InitializeAsync(CancellationToken.None);
            SetState(TranslationEngineState.Ready, "Google Translate 준비됨");
            _logger.Info("Google Translate engine ready");
        }
        catch (Exception ex)
        {
            SetState(TranslationEngineState.Failed, "Google Translate 초기화 실패");
            _logger.Error("Google Translate engine initialization failed", ex);
            throw;
        }
    }

    private void SetState(TranslationEngineState state, string message)
    {
        _state = state;
        _statusMessage = message;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
