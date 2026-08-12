namespace TranslationApp.Services.Clipboard;

public sealed class WindowsClipboardFacade : IClipboardFacade
{
    public string? GetText() => System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : null;
}
