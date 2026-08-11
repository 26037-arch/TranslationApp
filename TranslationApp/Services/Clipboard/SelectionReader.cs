using TranslationApp.Services.Logging;

namespace TranslationApp.Services.Clipboard;

public sealed class SelectionReader
{
    private readonly IClipboardFacade _clipboard;
    private readonly AppLogger _logger;
    private readonly ICopyShortcutSender _copySender;

    public SelectionReader(IClipboardFacade clipboard, AppLogger logger, ICopyShortcutSender? copySender = null)
    {
        _clipboard = clipboard;
        _logger = logger;
        _copySender = copySender ?? new WindowsCopyShortcutSender();
    }

    public async Task<string?> ReadExternalSelectionAsync(CancellationToken cancellationToken)
    {
        ClipboardSnapshot? snapshot = null;
        try
        {
            snapshot = await ClipboardRetry.RunAsync(
                () => ClipboardSnapshotFactory.Clone(_clipboard.GetDataObject()), cancellationToken);
            var sequence = _clipboard.GetSequenceNumber();
            _copySender.SendCopy();

            var deadline = DateTime.UtcNow.AddMilliseconds(2000);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_clipboard.GetSequenceNumber() != sequence)
                {
                    var text = await ClipboardRetry.RunAsync(_clipboard.GetText, cancellationToken);
                    return string.IsNullOrWhiteSpace(text) ? null : text;
                }
                await Task.Delay(25, cancellationToken);
            }
            return null;
        }
        finally
        {
            if (snapshot?.Data is not null)
            {
                try { await ClipboardRetry.RunAsync(() => { _clipboard.SetDataObject(snapshot.Data, true); return true; }, CancellationToken.None); }
                catch (Exception ex) { _logger.Error("클립보드 복원 실패", ex); }
            }
        }
    }
}
