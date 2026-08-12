using System.Threading.Channels;
using TranslationApp.Models;
using TranslationApp.Services.Logging;

namespace TranslationApp.Services.Translation;

public sealed class TranslationRequestQueue : ITranslator, IDisposable
{
    private readonly ITranslator _backend;
    private readonly AppLogger _logger;
    private readonly Channel<QueueItem> _channel = Channel.CreateUnbounded<QueueItem>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false
    });
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _shutdownToken;
    private readonly Task _processor;
    private int _accepting = 1;

    public TranslationRequestQueue(ITranslator backend, AppLogger logger)
    {
        _backend = backend;
        _logger = logger;
        _shutdownToken = _lifetime.Token;
        _processor = Task.Run(ProcessQueueAsync);
    }

    public async Task<TranslationResult> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _accepting) == 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        var completion = new TaskCompletionSource<TranslationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var item = new QueueItem(request, completion, cancellationToken);
        using var cancellationRegistration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));

        _logger.Info($"Queue enqueue request={request.RequestId:N}, length={request.OriginalText.Length}, " +
                     $"languages={request.SourceLanguage}->{request.TargetLanguage}");
        await _channel.Writer.WriteAsync(item, cancellationToken);
        return await completion.Task;
    }

    private async Task ProcessQueueAsync()
    {
        try
        {
            await foreach (var item in _channel.Reader.ReadAllAsync(_shutdownToken))
            {
                if (item.Completion.Task.IsCompleted) continue;
                _logger.Info($"Queue dequeue request={item.Request.RequestId:N}");

                using var requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(
                    _shutdownToken,
                    item.CancellationToken);
                try
                {
                    var result = await _backend.TranslateAsync(item.Request, requestLifetime.Token);
                    item.Completion.TrySetResult(result);
                }
                catch (OperationCanceledException) when (requestLifetime.IsCancellationRequested)
                {
                    item.Completion.TrySetCanceled(item.CancellationToken.IsCancellationRequested
                        ? item.CancellationToken
                        : _shutdownToken);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Queue request failed request={item.Request.RequestId:N}", ex);
                    item.Completion.TrySetException(ex);
                }
            }
        }
        catch (OperationCanceledException) when (_shutdownToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.Error("Translation queue processor stopped unexpectedly", ex);
        }
        finally
        {
            while (_channel.Reader.TryRead(out var item))
                item.Completion.TrySetCanceled(_shutdownToken);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _accepting, 0) == 0) return;
        _channel.Writer.TryComplete();
        _lifetime.Cancel();
        _ = _processor.ContinueWith(
            task => _logger.Error("Translation queue shutdown failed", task.Exception),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
        _ = _processor.ContinueWith(
            _ => _lifetime.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private sealed record QueueItem(
        TranslationRequest Request,
        TaskCompletionSource<TranslationResult> Completion,
        CancellationToken CancellationToken);
}
