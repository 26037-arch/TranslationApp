namespace TranslationApp.Models;

public sealed class OcrDocument
{
    public OcrDocument(string text) => Text = text;
    public Guid Id { get; } = Guid.NewGuid();
    public string Text { get; set; }
    public List<TranslationSegment> Segments { get; } = [];
}
