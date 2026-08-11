using TranslationApp.Services.Ocr;

namespace TranslationApp.Tests;

public sealed class OcrResultValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n")]
    public void EmptyOcrResultIsRejected(string? value) => Assert.Null(OcrResultValidator.Normalize(value));

    [Fact]
    public void NormalizesLineEndingsAndWhitespace() => Assert.Equal("hello\nworld", OcrResultValidator.Normalize(" hello\r\nworld \r\n"));
}
