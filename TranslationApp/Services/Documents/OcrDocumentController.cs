using TranslationApp.Models;

namespace TranslationApp.Services.Documents;

public sealed class OcrDocumentController(OcrDocument document, System.Windows.Controls.TextBox editor) : ITranslationDocumentHost
{
    private TranslationSegment? _programmaticSegment;

    public TranslationSegment CreateSegment(int start, int length, TargetLanguage target)
    {
        var segment = new TranslationSegment(editor.Text.Substring(start, length), target, start, length);
        document.Segments.Add(segment);
        return segment;
    }

    public void OnTextChanged(IReadOnlyList<DocumentChange> changes)
    {
        document.Text = editor.Text;
        foreach (var change in changes.OrderBy(x => x.Offset))
        {
            foreach (var segment in document.Segments.ToArray())
            {
                if (segment == _programmaticSegment)
                {
                    segment.UpdateRange(change.Offset, change.AddedLength,
                        change.Offset + change.AddedLength <= editor.Text.Length ? editor.Text.Substring(change.Offset, change.AddedLength) : string.Empty);
                }
                else SegmentAnchorTracker.Apply(segment, change, editor.Text);
            }
        }
    }

    public bool ReplaceSegmentText(TranslationSegment segment, string text)
    {
        if (segment.AttachmentState != TranslationAttachmentState.Attached || segment.Start < 0 || segment.Start + segment.Length > editor.Text.Length) return false;
        _programmaticSegment = segment;
        try
        {
            editor.Select(segment.Start, segment.Length);
            editor.SelectedText = text;
            segment.UpdateRange(segment.Start, text.Length, text);
            return true;
        }
        finally { _programmaticSegment = null; }
    }

    public bool RestoreOriginal(TranslationSegment segment) => ReplaceSegmentText(segment, segment.OriginalText);

    public void DetachAll()
    {
        foreach (var segment in document.Segments) segment.Detach();
    }
}
