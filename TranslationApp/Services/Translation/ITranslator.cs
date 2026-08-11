using TranslationApp.Models;

namespace TranslationApp.Services.Translation;

public interface ITranslator
{
    Task<TranslationResult> TranslateAsync(string text, TargetLanguage target, TranslationOptions options, CancellationToken cancellationToken);
}
