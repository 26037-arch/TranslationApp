using TranslationApp.Models;

namespace TranslationApp.Services.Translation;

public sealed class DummyTranslator : ITranslator, IModelLifecycle
{
    public ModelState State => ModelState.Ready;
    public string StatusMessage => "개발용 번역기 준비됨";
    public event EventHandler? StateChanged;
    public Task InitializeAsync(CancellationToken cancellationToken) { StateChanged?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }

    public Task<TranslationResult> TranslateAsync(string text, TargetLanguage target, TranslationOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<TranslationCandidate> candidates =
        [new($"[{(target == TargetLanguage.Korean ? "한국어" : "English")}] {text}")];
        return Task.FromResult(new TranslationResult(text, target, candidates, TimeSpan.Zero));
    }
}
