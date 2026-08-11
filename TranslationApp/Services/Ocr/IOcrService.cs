using System.Drawing;

namespace TranslationApp.Services.Ocr;

public interface IOcrService
{
    Task<string> RecognizeAsync(Bitmap image, CancellationToken cancellationToken);
}
