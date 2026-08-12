using System.Collections.ObjectModel;

namespace TranslationApp.Models;

public sealed class TranslationSession
{
    public Guid Id { get; } = Guid.NewGuid();
    public required string OriginalText { get; init; }
    public required TargetLanguage SourceLanguage { get; init; }
    public required TargetLanguage TargetLanguage { get; init; }
    public required InputSource Source { get; init; }
    public TranslationSegment? Segment { get; init; }
    public ObservableCollection<TranslationCandidate> Candidates { get; } = [];
}
