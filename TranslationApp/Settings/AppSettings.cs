namespace TranslationApp.Settings;

public sealed class AppSettings
{
    public string TesseractPath { get; set; } = @"C:\Program Files\Tesseract-OCR\tesseract.exe";
    public string TesseractDataPath { get; set; } = @"C:\Program Files\Tesseract-OCR\tessdata";
    public string OcrLanguages { get; set; } = "kor+eng";
    public bool StartWithWindows { get; set; }
}
