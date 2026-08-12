using TranslationApp.Models;

namespace TranslationApp.Services.Translation;

public static class GoogleTranslateLanguageCodes
{
    public static string GetCode(TargetLanguage language) => language switch
    {
        TargetLanguage.Korean => "ko",
        TargetLanguage.English => "en",
        _ => throw new ArgumentOutOfRangeException(nameof(language))
    };

    public static TargetLanguage SourceFor(TargetLanguage targetLanguage) => targetLanguage switch
    {
        TargetLanguage.Korean => TargetLanguage.English,
        TargetLanguage.English => TargetLanguage.Korean,
        _ => throw new ArgumentOutOfRangeException(nameof(targetLanguage))
    };
}
