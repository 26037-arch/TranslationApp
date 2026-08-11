using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TranslationApp.Models;
using TranslationApp.Services.Logging;
using TranslationApp.Settings;

namespace TranslationApp.Services.Translation;

public sealed class NllbTranslator : ITranslator, IModelLifecycle, IDisposable
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly AppSettings _settings;
    private readonly AppLogger _logger;
    private readonly string _workerPath;
    private readonly bool _testEchoMode;
    private readonly SemaphoreSlim _processGate = new(1, 1);
    private readonly object _stateGate = new();
    private Process? _worker;
    private Task? _initializeTask;
    private ModelState _state;
    private string _statusMessage = "시작 전";
    private bool _disposed;

    public NllbTranslator(AppSettings settings, AppLogger logger)
        : this(settings, logger, Path.Combine(AppContext.BaseDirectory, "Runtime", "nllb_worker.py"), false)
    {
    }

    internal NllbTranslator(AppSettings settings, AppLogger logger, string workerPath, bool testEchoMode)
    {
        _settings = settings;
        _logger = logger;
        _workerPath = workerPath;
        _testEchoMode = testEchoMode;
    }

    internal static Encoding IpcEncoding => Utf8NoBom;

    public ModelState State { get { lock (_stateGate) return _state; } private set { lock (_stateGate) _state = value; StateChanged?.Invoke(this, EventArgs.Empty); } }
    public string StatusMessage { get { lock (_stateGate) return _statusMessage; } private set { lock (_stateGate) _statusMessage = value; StateChanged?.Invoke(this, EventArgs.Empty); } }
    public event EventHandler? StateChanged;

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        lock (_stateGate) return _initializeTask ??= InitializeCoreAsync(cancellationToken);
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        State = ModelState.Loading;
        StatusMessage = "NLLB 번역 모델을 불러오는 중…";
        try
        {
            await StartWorkerAsync(cancellationToken);
            State = ModelState.Ready;
            StatusMessage = "NLLB INT8 CPU 모델 준비됨";
            _logger.Info(StatusMessage);
        }
        catch (Exception ex)
        {
            State = ModelState.Failed;
            StatusMessage = UserMessage(ex);
            _logger.Error("NLLB worker 초기화 실패", ex);
            throw;
        }
    }

    public async Task<TranslationResult> TranslateAsync(string text, TargetLanguage target, TranslationOptions options, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("번역할 텍스트가 비어 있습니다.", nameof(text));
        await InitializeAsync(cancellationToken);

        await _processGate.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try { return await SendRequestAsync(text, target, options, cancellationToken); }
                catch (OperationCanceledException)
                {
                    KillWorker();
                    ResetInitialization("번역 취소 후 worker 재시작 대기");
                    throw;
                }
                catch (Exception ex)
                {
                    if (attempt == 0)
                    {
                        _logger.Error("NLLB worker 요청 실패, 한 번 재시작합니다.", ex);
                        State = ModelState.Loading;
                        StatusMessage = "NLLB worker를 다시 시작하는 중…";
                        KillWorker();
                        try
                        {
                            await StartWorkerAsync(cancellationToken);
                            State = ModelState.Ready;
                            StatusMessage = "NLLB INT8 CPU 모델 준비됨";
                        }
                        catch (Exception restartException)
                        {
                            State = ModelState.Failed;
                            StatusMessage = UserMessage(restartException);
                            throw;
                        }
                    }
                    else
                    {
                        State = ModelState.Failed;
                        StatusMessage = UserMessage(ex);
                        throw;
                    }
                }
            }
            throw new InvalidOperationException("로컬 번역 worker가 응답하지 않습니다.");
        }
        finally { _processGate.Release(); }
    }

    private async Task StartWorkerAsync(CancellationToken cancellationToken)
    {
        KillWorker();
        if (!File.Exists(_workerPath)) throw new FileNotFoundException("NLLB worker 파일이 없습니다.", _workerPath);
        if (!_testEchoMode && !Directory.Exists(_settings.ModelPath))
            throw new DirectoryNotFoundException($"NLLB 모델 폴더가 없습니다: {_settings.ModelPath}. setup-runtime.ps1을 먼저 실행하세요.");

        var startInfo = new ProcessStartInfo
        {
            FileName = _settings.PythonExecutable,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = Utf8NoBom,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom
        };
        startInfo.ArgumentList.Add("-u");
        startInfo.ArgumentList.Add(_workerPath);
        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add(_settings.ModelPath);
        startInfo.ArgumentList.Add("--threads");
        startInfo.ArgumentList.Add(Math.Clamp(_settings.TorchThreads, 1, 8).ToString());
        if (_testEchoMode) startInfo.ArgumentList.Add("--self-test-echo");
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["TRANSFORMERS_OFFLINE"] = "1";
        startInfo.Environment["HF_HUB_OFFLINE"] = "1";

        _worker = Process.Start(startInfo) ?? throw new InvalidOperationException("Python worker를 시작할 수 없습니다.");
        _ = DrainStderrAsync(_worker);
        var line = await _worker.StandardOutput.ReadLineAsync(cancellationToken);
        if (line is null) throw new InvalidOperationException("NLLB worker가 준비 응답 전에 종료되었습니다.");
        using var ready = JsonDocument.Parse(line.TrimStart('\uFEFF'));
        var type = ready.RootElement.GetProperty("type").GetString();
        if (type == "error") throw new InvalidOperationException(ready.RootElement.GetProperty("message").GetString());
        if (type != "ready") throw new InvalidOperationException($"알 수 없는 worker 준비 응답: {line}");
    }

    private async Task<TranslationResult> SendRequestAsync(string text, TargetLanguage target, TranslationOptions options, CancellationToken cancellationToken)
    {
        var process = _worker;
        if (process is null || process.HasExited) throw new InvalidOperationException("NLLB worker가 실행 중이 아닙니다.");
        var id = Guid.NewGuid().ToString("N");
        var payload = JsonSerializer.Serialize(new
        {
            type = "translate", id, text,
            sourceLanguage = NllbLanguageCodes.SourceFor(target),
            targetLanguage = NllbLanguageCodes.Target(target),
            candidateCount = Math.Clamp(options.CandidateCount, 1, 4),
            beamWidth = Math.Clamp(options.BeamWidth, 1, 8),
            maxNewTokens = Math.Clamp(options.MaxNewTokens, 16, 512)
        });
        var stopwatch = Stopwatch.StartNew();
        await process.StandardInput.WriteLineAsync(payload.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
        var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
        if (line is null) throw new InvalidOperationException("NLLB worker 연결이 종료되었습니다.");
        using var response = JsonDocument.Parse(line.TrimStart('\uFEFF'));
        var root = response.RootElement;
        if (root.GetProperty("type").GetString() == "error")
            throw new InvalidOperationException(root.GetProperty("message").GetString());
        if (root.GetProperty("id").GetString() != id) throw new InvalidOperationException("NLLB worker 응답 ID가 일치하지 않습니다.");
        var candidates = root.GetProperty("candidates").EnumerateArray()
            .Select(item => new TranslationCandidate(
                item.GetProperty("text").GetString() ?? string.Empty,
                item.TryGetProperty("score", out var score) && score.ValueKind == JsonValueKind.Number ? score.GetDouble() : null))
            .Where(x => !string.IsNullOrWhiteSpace(x.Text))
            .DistinctBy(x => x.Text, StringComparer.Ordinal)
            .ToList();
        if (candidates.Count == 0) throw new InvalidOperationException("번역 결과가 비어 있습니다.");
        return new TranslationResult(text, target, candidates, stopwatch.Elapsed);
    }

    private async Task DrainStderrAsync(Process process)
    {
        try
        {
            while (await process.StandardError.ReadLineAsync() is { } line) _logger.Info($"[NLLB worker] {line}");
        }
        catch (Exception ex) { _logger.Error("NLLB worker stderr 읽기 실패", ex); }
    }

    private static string UserMessage(Exception ex)
    {
        var message = ex.Message;
        if (message.Contains("No module named", StringComparison.OrdinalIgnoreCase))
            return "Python 번역 의존성이 없습니다. setup-runtime.ps1을 실행하세요.";
        if (ex is DirectoryNotFoundException) return message;
        return "NLLB 모델을 불러오지 못했습니다. 설정과 로그를 확인하세요.";
    }

    private void ResetInitialization(string message)
    {
        lock (_stateGate) _initializeTask = null;
        State = ModelState.NotStarted;
        StatusMessage = message;
    }

    private void KillWorker()
    {
        var process = Interlocked.Exchange(ref _worker, null);
        if (process is null) return;
        try { if (!process.HasExited) process.Kill(true); } catch { }
        process.Dispose();
    }

    internal void TerminateWorkerForTest() => KillWorker();

    public void Dispose()
    {
        _disposed = true;
        KillWorker();
        _processGate.Dispose();
    }
}
