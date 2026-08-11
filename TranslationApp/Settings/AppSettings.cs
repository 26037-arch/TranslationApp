namespace TranslationApp.Settings;

public sealed class AppSettings
{
    public string TesseractPath { get; set; } = @"C:\Program Files\Tesseract-OCR\tesseract.exe";
    public string TesseractDataPath { get; set; } = @"C:\Program Files\Tesseract-OCR\tessdata";
    public string OcrLanguages { get; set; } = "kor+eng";
    public string PythonExecutable { get; set; } = "python";
    public string ModelPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TranslationApp", "models", "nllb-200-distilled-600M");
    public int TorchThreads { get; set; } = 4;
    public bool StartWithWindows { get; set; }
}
