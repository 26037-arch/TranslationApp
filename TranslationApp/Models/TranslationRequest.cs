namespace TranslationApp.Models;

public sealed record TranslationRequest(
    Guid RequestId,
    string OriginalText,
    TargetLanguage SourceLanguage,
    TargetLanguage TargetLanguage,
    InputSource InputSource)
{
    public static TranslationRequest Create(
        string originalText,
        TargetLanguage sourceLanguage,
        TargetLanguage targetLanguage,
        InputSource inputSource)
    {
        if (string.IsNullOrWhiteSpace(originalText))
            throw new ArgumentException("번역할 텍스트가 비어 있습니다.", nameof(originalText));
        if (!Enum.IsDefined(sourceLanguage))
            throw new ArgumentOutOfRangeException(nameof(sourceLanguage));
        if (!Enum.IsDefined(targetLanguage))
            throw new ArgumentOutOfRangeException(nameof(targetLanguage));
        if (sourceLanguage == targetLanguage)
            throw new ArgumentException("출발 언어와 도착 언어는 달라야 합니다.", nameof(targetLanguage));

        return new TranslationRequest(Guid.NewGuid(), originalText, sourceLanguage, targetLanguage, inputSource);
    }
}
