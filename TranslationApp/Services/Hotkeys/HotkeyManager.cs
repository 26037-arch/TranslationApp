using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace TranslationApp.Services.Hotkeys;

public sealed class HotkeyManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private readonly HwndSource _source;
    private readonly Dictionary<int, HotkeyAction> _actions = new();

    public HotkeyManager()
    {
        var parameters = new HwndSourceParameters("TranslationApp.Hotkeys")
        {
            ParentWindow = new IntPtr(-3),
            WindowStyle = 0
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public event EventHandler<HotkeyAction>? Pressed;

    public void RegisterDefaults()
    {
        Register(1, HotkeyAction.TranslateToEnglish, 0x45); // E
        Register(2, HotkeyAction.TranslateToKorean, 0x4B);  // K
        Register(3, HotkeyAction.CaptureOcr, 0x4F);          // O
    }

    private void Register(int id, HotkeyAction action, uint virtualKey)
    {
        if (!RegisterHotKey(_source.Handle, id, ModControl | ModAlt | ModNoRepeat, virtualKey))
            throw new InvalidOperationException($"Ctrl+Alt+{(char)virtualKey} 단축키를 등록할 수 없습니다. 다른 프로그램에서 사용 중일 수 있습니다.");
        _actions[id] = action;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            Pressed?.Invoke(this, action);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys) UnregisterHotKey(_source.Handle, id);
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
