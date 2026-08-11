using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace TranslationApp.Views;

public partial class ToastWindow : Window
{
    public ToastWindow(string message, bool isError)
    {
        InitializeComponent();
        MessageText.Text = message;
        Card.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(isError ? "#991B1B" : "#1E293B"));
        Loaded += (_, _) =>
        {
            var area = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
            var dpi = VisualTreeHelper.GetDpi(this);
            var width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
            var height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
            SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), area.Right - width - 18, area.Bottom - height - 18,
                width, height, 0x0010 | 0x0040);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (_, _) => { timer.Stop(); Close(); };
            timer.Start();
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
