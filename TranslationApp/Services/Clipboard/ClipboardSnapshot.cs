namespace TranslationApp.Services.Clipboard;

public sealed class ClipboardSnapshot
{
    public ClipboardSnapshot(System.Windows.IDataObject? data) => Data = data;
    public System.Windows.IDataObject? Data { get; }
}
