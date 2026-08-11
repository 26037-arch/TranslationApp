namespace TranslationApp.Models;

public sealed record TranslationResult(
    string OriginalText,
    TargetLanguage TargetLanguage,
    IReadOnlyList<TranslationCandidate> Candidates,
    TimeSpan Elapsed)
{
    public TranslationCandidate Primary => Candidates.First();
}
