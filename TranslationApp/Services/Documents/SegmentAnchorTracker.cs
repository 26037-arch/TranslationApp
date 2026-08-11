using TranslationApp.Models;

namespace TranslationApp.Services.Documents;

public readonly record struct DocumentChange(int Offset, int RemovedLength, int AddedLength);

public static class SegmentAnchorTracker
{
    public static void Apply(TranslationSegment segment, DocumentChange change, string newDocumentText)
    {
        if (segment.AttachmentState != TranslationAttachmentState.Attached) return;
        var start = segment.Start;
        var end = start + segment.Length;
        var removedEnd = change.Offset + change.RemovedLength;
        var delta = change.AddedLength - change.RemovedLength;

        if (change.RemovedLength > 0 && change.AddedLength == 0 && change.Offset <= start && removedEnd >= end)
        {
            segment.Detach();
            return;
        }

        if (removedEnd <= start || (change.RemovedLength == 0 && change.Offset <= start))
        {
            start += delta;
            end += delta;
        }
        else if (change.Offset < end)
        {
            start = MapStart(start, change);
            end = MapEnd(end, change);
            if (end <= start) { segment.Detach(); return; }
        }

        var length = Math.Max(0, end - start);
        if (start < 0 || start + length > newDocumentText.Length) { segment.Detach(); return; }
        segment.UpdateRange(start, length, newDocumentText.Substring(start, length));
    }

    private static int MapStart(int position, DocumentChange change)
    {
        if (position < change.Offset) return position;
        if (position >= change.Offset + change.RemovedLength) return position + change.AddedLength - change.RemovedLength;
        return change.Offset;
    }

    private static int MapEnd(int position, DocumentChange change)
    {
        if (position <= change.Offset) return position;
        if (position >= change.Offset + change.RemovedLength) return position + change.AddedLength - change.RemovedLength;
        return change.Offset + change.AddedLength;
    }
}
