using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using TranslationApp.Settings;

namespace TranslationApp.Services.Ocr;

public sealed class TesseractOcrService(AppSettings settings) : IOcrService
{
    public async Task<string> RecognizeAsync(Bitmap image, CancellationToken cancellationToken)
    {
        ValidateInstallation();
        var tempPath = Path.Combine(Path.GetTempPath(), $"TranslationApp-{Guid.NewGuid():N}.png");
        try
        {
            image.Save(tempPath, ImageFormat.Png);
            var startInfo = new ProcessStartInfo
            {
                FileName = settings.TesseractPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };
            startInfo.ArgumentList.Add(tempPath);
            startInfo.ArgumentList.Add("stdout");
            startInfo.ArgumentList.Add("--tessdata-dir");
            startInfo.ArgumentList.Add(settings.TesseractDataPath);
            startInfo.ArgumentList.Add("-l");
            startInfo.ArgumentList.Add(settings.OcrLanguages);
            startInfo.ArgumentList.Add("--psm");
            startInfo.ArgumentList.Add("6");

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Tesseract를 시작할 수 없습니다.");
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = (await outputTask).Trim();
            var error = await errorTask;
            if (process.ExitCode != 0) throw new InvalidOperationException($"Tesseract OCR 실패: {error.Trim()}");
            return output;
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    private void ValidateInstallation()
    {
        if (!File.Exists(settings.TesseractPath)) throw new FileNotFoundException("Tesseract 실행 파일이 없습니다.", settings.TesseractPath);
        if (!Directory.Exists(settings.TesseractDataPath)) throw new DirectoryNotFoundException($"tessdata 폴더가 없습니다: {settings.TesseractDataPath}");
        foreach (var language in settings.OcrLanguages.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var path = Path.Combine(settings.TesseractDataPath, language + ".traineddata");
            if (!File.Exists(path)) throw new FileNotFoundException($"OCR 언어 데이터가 없습니다: {language}", path);
        }
    }
}
