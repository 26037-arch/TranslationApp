using TranslationApp.Models;
using TranslationApp.Services.Translation;

namespace TranslationApp.Tests;

public sealed class LanguageMappingTests
{
    [Theory]
    [InlineData(TargetLanguage.Korean, "ko", TargetLanguage.English)]
    [InlineData(TargetLanguage.English, "en", TargetLanguage.Korean)]
    public void MapsApplicationLanguagesToGoogleCodesAndOppositeSource(
        TargetLanguage target,
        string targetCode,
        TargetLanguage source)
    {
        Assert.Equal(targetCode, GoogleTranslateLanguageCodes.GetCode(target));
        Assert.Equal(source, GoogleTranslateLanguageCodes.SourceFor(target));
    }
}
