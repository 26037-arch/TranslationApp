using TranslationApp.Models;

namespace TranslationApp.Services.Translation;

public static class NllbLanguageCodes
{
    public static string Target(TargetLanguage language) => language switch
    {
        TargetLanguage.Korean => "kor_Hang",
        TargetLanguage.English => "eng_Latn",
        _ => throw new ArgumentOutOfRangeException(nameof(language))
    };

    public static string SourceFor(TargetLanguage target) => target switch
    {
        TargetLanguage.Korean => "eng_Latn",
        TargetLanguage.English => "kor_Hang",
        _ => throw new ArgumentOutOfRangeException(nameof(target))
    };
}
