using System.Drawing;

namespace TranslationApp.Services.Ocr;

public sealed class ScreenCaptureResult(Bitmap bitmap, Rectangle screenBounds) : IDisposable
{
    public Bitmap Bitmap { get; } = bitmap;
    public Rectangle ScreenBounds { get; } = screenBounds;
    public Bitmap Crop(Rectangle relativePixels) => Bitmap.Clone(relativePixels, Bitmap.PixelFormat);
    public void Dispose() => Bitmap.Dispose();
}
