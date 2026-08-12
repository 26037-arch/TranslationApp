namespace TranslationApp.Models;

public sealed record TranslationResult(
    Guid RequestId,
    string OriginalText,
    TargetLanguage SourceLanguage,
    TargetLanguage TargetLanguage,
    IReadOnlyList<TranslationCandidate> Candidates,
    TimeSpan Elapsed)
{
    public TranslationCandidate Primary => Candidates.First();
}
