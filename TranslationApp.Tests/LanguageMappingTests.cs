using TranslationApp.Models;
using TranslationApp.Services.Translation;

namespace TranslationApp.Tests;

public sealed class LanguageMappingTests
{
    [Theory]
    [InlineData(TargetLanguage.Korean, "kor_Hang", "eng_Latn")]
    [InlineData(TargetLanguage.English, "eng_Latn", "kor_Hang")]
    public void MapsApplicationLanguagesToNllbCodes(TargetLanguage target, string targetCode, string sourceCode)
    {
        Assert.Equal(targetCode, NllbLanguageCodes.Target(target));
        Assert.Equal(sourceCode, NllbLanguageCodes.SourceFor(target));
    }
}
