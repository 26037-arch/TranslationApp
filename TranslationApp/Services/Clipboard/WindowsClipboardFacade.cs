using System.Runtime.InteropServices;
namespace TranslationApp.Services.Clipboard;

public sealed class WindowsClipboardFacade : IClipboardFacade
{
    public System.Windows.IDataObject? GetDataObject() => System.Windows.Clipboard.GetDataObject();
    public void SetDataObject(System.Windows.IDataObject data, bool copy) => System.Windows.Clipboard.SetDataObject(data, copy);
    public string? GetText() => System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null;
    public uint GetSequenceNumber() => GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
}
