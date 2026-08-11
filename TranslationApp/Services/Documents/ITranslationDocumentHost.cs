using TranslationApp.Models;

namespace TranslationApp.Services.Documents;

public interface ITranslationDocumentHost
{
    bool ReplaceSegmentText(TranslationSegment segment, string text);
    bool RestoreOriginal(TranslationSegment segment);
}
