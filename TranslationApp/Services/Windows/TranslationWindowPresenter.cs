using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using TranslationApp.Views;

namespace TranslationApp.Services.Windows;

public sealed class TranslationWindowPresenter(WindowRegistry registry)
{
    public void ShowNear(TranslationWindow window, Rectangle anchor)
    {
        var screen = Screen.FromRectangle(anchor);
        var scale = GetMonitorScale(anchor);
        var size = new Size((int)Math.Round(window.Width * scale), (int)Math.Round(window.Height * scale));
        var occupied = registry.TranslationWindows.Select(GetBounds).Where(x => x.Width > 0).ToList();
        var placement = WindowPlacementCalculator.Calculate(anchor, size, screen.WorkingArea, occupied);
        registry.Add(window);
        window.ShowActivated = false;
        window.Show();
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowPos(handle, IntPtr.Zero, placement.X, placement.Y, placement.Width, placement.Height,
            SwpNoZOrder | SwpNoActivate | SwpShowWindow);
    }

    private static double GetMonitorScale(Rectangle anchor)
    {
        try
        {
            var monitor = MonitorFromPoint(new NativePoint(anchor.Left, anchor.Top), 2);
            if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0) return dpiX / 96.0;
        }
        catch { }
        return 1.0;
    }

    private static Rectangle GetBounds(TranslationWindow window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        return handle != IntPtr.Zero && GetWindowRect(handle, out var rect)
            ? Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom)
            : Rectangle.Empty;
    }

    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
    [StructLayout(LayoutKind.Sequential)] private readonly struct NativePoint(int x, int y) { public readonly int X = x; public readonly int Y = y; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
