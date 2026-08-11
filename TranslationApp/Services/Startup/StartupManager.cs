using Microsoft.Win32;

namespace TranslationApp.Services.Startup;

public sealed class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TranslationApp";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(ValueName) is string;
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (!enabled) { key.DeleteValue(ValueName, false); return; }
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("실행 파일 경로를 확인할 수 없습니다.");
        key.SetValue(ValueName, $"\"{executable}\" --startup", RegistryValueKind.String);
    }
}
