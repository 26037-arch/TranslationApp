using System.Runtime.InteropServices;
using TranslationApp.Services.Clipboard;

namespace TranslationApp.Tests;

public sealed class ClipboardTextReaderTests
{
    [Fact]
    public async Task ReadsCurrentClipboardText()
    {
        var clipboard = new FakeClipboard("현재 클립보드: 한글 & English <>");
        var reader = new ClipboardTextReader(clipboard);

        var text = await reader.ReadTextAsync(CancellationToken.None);

        Assert.Equal("현재 클립보드: 한글 & English <>", text);
        Assert.Equal(1, clipboard.ReadAttempts);
    }

    [Fact]
    public async Task ReturnsNullForNonTextClipboardContent()
    {
        var reader = new ClipboardTextReader(new FakeClipboard(null));

        var text = await reader.ReadTextAsync(CancellationToken.None);

        Assert.Null(text);
    }

    [Fact]
    public async Task ClipboardLockIsRetriedDuringTextRead()
    {
        var clipboard = new FakeClipboard("잠긴 뒤 읽은 텍스트", failures: 3);
        var reader = new ClipboardTextReader(clipboard);

        var text = await reader.ReadTextAsync(CancellationToken.None);

        Assert.Equal("잠긴 뒤 읽은 텍스트", text);
        Assert.Equal(4, clipboard.ReadAttempts);
    }

    private sealed class FakeClipboard(string? text, int failures = 0) : IClipboardFacade
    {
        public int ReadAttempts { get; private set; }

        public string? GetText()
        {
            ReadAttempts++;
            if (ReadAttempts <= failures)
                throw new COMException("OpenClipboard failed", unchecked((int)0x800401D0));
            return text;
        }
    }
}
