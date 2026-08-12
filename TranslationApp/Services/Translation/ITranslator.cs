using TranslationApp.Models;

namespace TranslationApp.Services.Translation;

public interface ITranslator
{
    Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken);
}
