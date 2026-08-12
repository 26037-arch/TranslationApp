using TranslationApp.Models;

namespace TranslationApp.Services.Translation;

internal interface IGoogleTranslateWebClient
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<GoogleTranslateDomResult> TranslateOnceAsync(
        TranslationRequest request,
        Uri address,
        int attempt,
        CancellationToken cancellationToken);
}
