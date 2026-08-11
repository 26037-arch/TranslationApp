using TranslationApp.Models;
using TranslationApp.Services.Documents;

namespace TranslationApp.Tests;

public sealed class SegmentAnchorTrackerTests
{
    [Fact]
    public void EditBeforeSegmentMovesAnchorAndPreservesConnection()
    {
        var segment = new TranslationSegment("world", TargetLanguage.Korean, 6, 5);
        SegmentAnchorTracker.Apply(segment, new DocumentChange(0, 0, 2), "++hello world");
        Assert.Equal(8, segment.Start);
        Assert.Equal("world", segment.CurrentText);
        Assert.Equal(TranslationAttachmentState.Attached, segment.AttachmentState);
    }

    [Fact]
    public void EditInsideSegmentUpdatesCurrentText()
    {
        var segment = new TranslationSegment("world", TargetLanguage.Korean, 6, 5);
        SegmentAnchorTracker.Apply(segment, new DocumentChange(7, 1, 2), "hello woorld");
        Assert.Equal(6, segment.Start);
        Assert.Equal(6, segment.Length);
        Assert.Equal("woorld", segment.CurrentText);
    }

    [Fact]
    public void CompleteDeletionDetachesSegment()
    {
        var segment = new TranslationSegment("world", TargetLanguage.Korean, 6, 5);
        SegmentAnchorTracker.Apply(segment, new DocumentChange(6, 5, 0), "hello ");
        Assert.Equal(TranslationAttachmentState.Detached, segment.AttachmentState);
        Assert.Equal("world", segment.OriginalText);
    }

    [Fact]
    public void ReplacingEntireSegmentKeepsConnection()
    {
        var segment = new TranslationSegment("world", TargetLanguage.Korean, 6, 5);
        SegmentAnchorTracker.Apply(segment, new DocumentChange(6, 5, 2), "hello 지구");
        Assert.Equal(TranslationAttachmentState.Attached, segment.AttachmentState);
        Assert.Equal(6, segment.Start);
        Assert.Equal("지구", segment.CurrentText);
    }

    [Fact]
    public void UserEditedTranslationStaysInCurrentText()
    {
        var segment = new TranslationSegment("quantum mechanics", TargetLanguage.Korean, 0, 17);
        segment.ReplaceCurrent("양자 역학");
        Assert.Equal("양자 역학", segment.CurrentText);
        Assert.Equal("quantum mechanics", segment.OriginalText);
    }
}
