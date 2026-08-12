using TranslationApp.Models;

namespace TranslationApp.Services.Translation;

public static class GoogleTranslateUrlBuilder
{
    public const int MaximumInputCharacters = 5_000;
    public const int MaximumUriCharacters = 60_000;
    private const string BaseAddress = "https://translate.google.com/";

    public static Uri Build(TranslationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.OriginalText))
            throw new ArgumentException("번역할 텍스트가 비어 있습니다.", nameof(request));
        if (request.SourceLanguage == request.TargetLanguage)
            throw new ArgumentException("출발 언어와 도착 언어는 달라야 합니다.", nameof(request));
        if (request.OriginalText.Length > MaximumInputCharacters)
            throw new ArgumentException(
                $"번역할 텍스트는 {MaximumInputCharacters:N0}자를 넘을 수 없습니다. 텍스트를 나누어 시도하세요.",
                nameof(request));

        var source = GoogleTranslateLanguageCodes.GetCode(request.SourceLanguage);
        var target = GoogleTranslateLanguageCodes.GetCode(request.TargetLanguage);
        var encodedText = Uri.EscapeDataString(request.OriginalText);
        var address = $"{BaseAddress}?sl={source}&tl={target}&text={encodedText}&op=translate";
        if (address.Length > MaximumUriCharacters)
            throw new ArgumentException(
                "번역 요청 URL이 너무 깁니다. 텍스트를 나누어 시도하세요.",
                nameof(request));

        return new Uri(address, UriKind.Absolute);
    }
}
