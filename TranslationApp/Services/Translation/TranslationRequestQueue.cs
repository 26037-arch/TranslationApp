using TranslationApp.Models;

namespace TranslationApp.Services.Translation;

public sealed class TranslationRequestQueue(ITranslator backend) : ITranslator, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<TranslationResult> TranslateAsync(string text, TargetLanguage target, TranslationOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("번역할 텍스트가 비어 있습니다.", nameof(text));
        await _gate.WaitAsync(cancellationToken);
        try { return await backend.TranslateAsync(text, target, options, cancellationToken); }
        finally { _gate.Release(); }
    }

    public void Dispose() => _gate.Dispose();
}
