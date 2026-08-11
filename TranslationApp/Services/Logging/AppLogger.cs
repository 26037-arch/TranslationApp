namespace TranslationApp.Services.Logging;

public sealed class AppLogger
{
    private readonly object _gate = new();
    private readonly string _folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TranslationApp", "logs");

    public void Info(string message) => Write("INFO", message, null);
    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            Directory.CreateDirectory(_folder);
            var path = Path.Combine(_folder, $"translationapp-{DateTime.Now:yyyyMMdd}.log");
            var line = $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}";
            if (exception is not null) line += exception + Environment.NewLine;
            lock (_gate) File.AppendAllText(path, line);
        }
        catch { }
    }
}
