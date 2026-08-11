namespace TranslationApp.Services.Clipboard;

public interface IClipboardFacade
{
    System.Windows.IDataObject? GetDataObject();
    void SetDataObject(System.Windows.IDataObject data, bool copy);
    string? GetText();
    uint GetSequenceNumber();
}
