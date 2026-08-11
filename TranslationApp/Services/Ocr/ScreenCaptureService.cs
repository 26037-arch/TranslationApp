using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace TranslationApp.Services.Ocr;

public sealed class ScreenCaptureService
{
    public ScreenCaptureResult CaptureCurrentMonitor()
    {
        var screen = Screen.FromPoint(Cursor.Position);
        var bounds = screen.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) throw new InvalidOperationException("캡처할 화면 크기가 올바르지 않습니다.");
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            return new ScreenCaptureResult(bitmap, bounds);
        }
        catch { bitmap.Dispose(); throw; }
    }
}
