namespace TranslationApp.Models;

public sealed record TranslationOptions(int CandidateCount = 1, int BeamWidth = 4, int MaxNewTokens = 256)
{
    public static TranslationOptions Primary { get; } = new(1, 4, 256);
    public static TranslationOptions Alternatives { get; } = new(4, 6, 256);
}
