namespace TranslationApp.Services.Clipboard;

public sealed class ClipboardTextReader(IClipboardFacade clipboard)
{
    public async Task<string?> ReadTextAsync(CancellationToken cancellationToken)
    {
        var text = await ClipboardRetry.RunAsync(clipboard.GetText, cancellationToken);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
