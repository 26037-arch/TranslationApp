using TranslationApp.Models;
using TranslationApp.Services.Logging;
using TranslationApp.Services.Notifications;
using TranslationApp.Services.Translation;
using TranslationApp.ViewModels;

namespace TranslationApp.Tests;

public sealed class TranslationViewModelTests
{
    [Fact]
    public async Task ClosingViewModelCancelsPendingTranslationWithoutSurfacingFailure()
    {
        var translator = new BlockingTranslator();
        var session = new TranslationSession
        {
            OriginalText = "hello",
            SourceLanguage = TargetLanguage.English,
            TargetLanguage = TargetLanguage.Korean,
            Source = InputSource.Clipboard
        };
        var viewModel = new TranslationViewModel(
            session,
            translator,
            null,
            new NotificationService(),
            new AppLogger());
        var request = TranslationRequest.Create(
            session.OriginalText,
            session.SourceLanguage,
            session.TargetLanguage,
            session.Source);
        var pending = viewModel.StartTranslationAsync(request);
        await translator.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        viewModel.Dispose();

        await pending.WaitAsync(TimeSpan.FromSeconds(2));
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
