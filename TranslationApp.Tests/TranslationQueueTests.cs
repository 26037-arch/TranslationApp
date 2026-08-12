using TranslationApp.Models;
using TranslationApp.Services.Logging;
using TranslationApp.Services.Translation;

namespace TranslationApp.Tests;

public sealed class TranslationQueueTests
{
    [Fact]
    public async Task ProcessesRequestsInStrictFifoOrderWithoutConcurrency()
    {
        var backend = new TrackingTranslator();
        using var queue = new TranslationRequestQueue(backend, new AppLogger());
        var requests = Enumerable.Range(0, 5).Select(CreateRequest).ToArray();

        var results = await Task.WhenAll(requests.Select(request =>
            queue.TranslateAsync(request, CancellationToken.None)));

        Assert.Equal(requests.Select(x => x.RequestId), backend.StartedRequestIds);
        Assert.Equal(requests.Select(x => x.RequestId), results.Select(x => x.RequestId));
        Assert.Equal(1, backend.MaxConcurrency);
    }

    [Fact]
    public async Task WaitingRequestCanBeCancelled()
    {
        var backend = new TrackingTranslator(200);
        using var queue = new TranslationRequestQueue(backend, new AppLogger());
        var first = queue.TranslateAsync(CreateRequest(1), CancellationToken.None);
        using var cts = new CancellationTokenSource(20);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            queue.TranslateAsync(CreateRequest(2), cts.Token));
        await first;
    }

    [Fact]
    public async Task FailedRequestDoesNotStopFollowingRequests()
    {
        var backend = new TrackingTranslator(failText: "text1");
        using var queue = new TranslationRequestQueue(backend, new AppLogger());
        var failed = queue.TranslateAsync(CreateRequest(1), CancellationToken.None);
        var succeeded = queue.TranslateAsync(CreateRequest(2), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => failed);
        Assert.Equal("text2", (await succeeded).Primary.Text);
    }

    [Fact]
    public async Task DisposeCancelsRunningAndWaitingRequests()
    {
        var backend = new BlockingTranslator();
        var queue = new TranslationRequestQueue(backend, new AppLogger());
        var running = queue.TranslateAsync(CreateRequest(1), CancellationToken.None);
        var waiting = queue.TranslateAsync(CreateRequest(2), CancellationToken.None);
        await backend.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        queue.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    private static TranslationRequest CreateRequest(int index) => TranslationRequest.Create(
        $"text{index}",
        TargetLanguage.English,
        TargetLanguage.Korean,
        InputSource.Clipboard);

    private sealed class TrackingTranslator(int delayMs = 35, string? failText = null) : ITranslator
    {
        private int _active;
        public int MaxConcurrency { get; private set; }
        public List<Guid> StartedRequestIds { get; } = [];

        public async Task<TranslationResult> TranslateAsync(
            TranslationRequest request,
            CancellationToken cancellationToken)
        {
            StartedRequestIds.Add(request.RequestId);
            var active = Interlocked.Increment(ref _active);
            MaxConcurrency = Math.Max(MaxConcurrency, active);
            try
            {
                await Task.Delay(delayMs, cancellationToken);
                if (request.OriginalText == failText) throw new InvalidOperationException("expected failure");
                return new TranslationResult(
                    request.RequestId,
                    request.OriginalText,
                    request.SourceLanguage,
                    request.TargetLanguage,
                    [new TranslationCandidate(request.OriginalText)],
                    TimeSpan.Zero);
            }
            finally { Interlocked.Decrement(ref _active); }
        }
    }

    private sealed class BlockingTranslator : ITranslator
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<TranslationResult> TranslateAsync(
            TranslationRequest request,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }
}
