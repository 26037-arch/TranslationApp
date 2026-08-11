using System.Windows;
using System.Runtime.InteropServices;
using TranslationApp.Services.Clipboard;
using TranslationApp.Services.Logging;

namespace TranslationApp.Tests;

public sealed class ClipboardSelectionTests
{
    [Fact]
    public async Task SelectionReadRestoresAllOriginalFormats()
    {
        var original = new DataObject();
        original.SetData(DataFormats.Text, "old text");
        original.SetData("TranslationApp.TestFormat", "binary-like-data");
        var clipboard = new FakeClipboard(original);
        var copy = new FakeCopySender(() => clipboard.ReplaceWithText("selected text"));
        var reader = new SelectionReader(clipboard, new AppLogger(), copy);

        var selected = await reader.ReadExternalSelectionAsync(CancellationToken.None);

        Assert.Equal("selected text", selected);
        Assert.Equal("old text", clipboard.Data.GetData(DataFormats.Text));
        Assert.Equal("binary-like-data", clipboard.Data.GetData("TranslationApp.TestFormat"));
    }

    [Fact]
    public async Task ClipboardLockIsRetriedDuringBackupReadAndRestore()
    {
        var original = new DataObject();
        original.SetData(DataFormats.Text, "original clipboard");
        var clipboard = new LockingClipboard(original, backupFailures: 3, readFailures: 2, restoreFailures: 3);
        var copy = new FakeCopySender(() => clipboard.ReplaceWithText("잠긴 뒤 copied: 한글 & English <>"));
        var reader = new SelectionReader(clipboard, new AppLogger(), copy);

        var selected = await reader.ReadExternalSelectionAsync(CancellationToken.None);

        Assert.Equal("잠긴 뒤 copied: 한글 & English <>", selected);
        Assert.Equal("original clipboard", clipboard.Data.GetData(DataFormats.Text));
        Assert.Equal(4, clipboard.BackupAttempts);
        Assert.Equal(3, clipboard.ReadAttempts);
        Assert.Equal(4, clipboard.RestoreAttempts);
    }

    private sealed class FakeCopySender(Action action) : ICopyShortcutSender { public void SendCopy() => action(); }
    private sealed class FakeClipboard(IDataObject data) : IClipboardFacade
    {
        private uint _sequence = 1;
        public IDataObject Data { get; private set; } = data;
        public IDataObject? GetDataObject() => Data;
        public void SetDataObject(IDataObject value, bool copy) { Data = value; _sequence++; }
        public string? GetText() => Data.GetDataPresent(DataFormats.Text) ? Data.GetData(DataFormats.Text)?.ToString() : null;
        public uint GetSequenceNumber() => _sequence;
        public void ReplaceWithText(string text) { var next = new DataObject(); next.SetData(DataFormats.Text, text); Data = next; _sequence++; }
    }

    private sealed class LockingClipboard(
        IDataObject data,
        int backupFailures,
        int readFailures,
        int restoreFailures) : IClipboardFacade
    {
        private uint _sequence = 1;
        public IDataObject Data { get; private set; } = data;
        public int BackupAttempts { get; private set; }
        public int ReadAttempts { get; private set; }
        public int RestoreAttempts { get; private set; }

        public IDataObject? GetDataObject()
        {
            BackupAttempts++;
            if (BackupAttempts <= backupFailures) throw Locked();
            return Data;
        }

        public void SetDataObject(IDataObject value, bool copy)
        {
            RestoreAttempts++;
            if (RestoreAttempts <= restoreFailures) throw Locked();
            Data = value;
            _sequence++;
        }

        public string? GetText()
        {
            ReadAttempts++;
            if (ReadAttempts <= readFailures) throw Locked();
            return Data.GetDataPresent(DataFormats.Text) ? Data.GetData(DataFormats.Text)?.ToString() : null;
        }

        public uint GetSequenceNumber() => _sequence;
        public void ReplaceWithText(string text)
        {
            var next = new DataObject();
            next.SetData(DataFormats.Text, text);
            Data = next;
            _sequence++;
        }

        private static COMException Locked() => new("OpenClipboard failed", unchecked((int)0x800401D0));
    }
}
