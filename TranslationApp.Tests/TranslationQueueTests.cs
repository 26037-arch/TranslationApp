using TranslationApp.Models;
using TranslationApp.Services.Translation;

namespace TranslationApp.Tests;

public sealed class TranslationQueueTests
{
    [Fact]
    public async Task SerializesConcurrentCpuRequests()
    {
        var backend = new TrackingTranslator();
        using var queue = new TranslationRequestQueue(backend);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(i =>
            queue.TranslateAsync($"text{i}", TargetLanguage.Korean, TranslationOptions.Primary, CancellationToken.None)));
        Assert.Equal(1, backend.MaxConcurrency);
    }

    [Fact]
    public async Task WaitingRequestCanBeCancelled()
    {
        var backend = new TrackingTranslator(200);
        using var queue = new TranslationRequestQueue(backend);
        var first = queue.TranslateAsync("one", TargetLanguage.Korean, TranslationOptions.Primary, CancellationToken.None);
        using var cts = new CancellationTokenSource(20);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            queue.TranslateAsync("two", TargetLanguage.Korean, TranslationOptions.Primary, cts.Token));
        await first;
    }

    private sealed class TrackingTranslator(int delayMs = 35) : ITranslator
    {
        private int _active;
        public int MaxConcurrency { get; private set; }
        public async Task<TranslationResult> TranslateAsync(string text, TargetLanguage target, TranslationOptions options, CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _active);
            MaxConcurrency = Math.Max(MaxConcurrency, active);
            try
            {
                await Task.Delay(delayMs, cancellationToken);
                return new TranslationResult(text, target, [new(text)], TimeSpan.Zero);
            }
            finally { Interlocked.Decrement(ref _active); }
        }
    }
}
