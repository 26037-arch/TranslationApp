namespace TranslationApp.Services.Ocr;

public static class OcrResultValidator
{
    public static string? Normalize(string? text)
    {
        var normalized = text?.Replace("\r\n", "\n").Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
