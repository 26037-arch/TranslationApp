using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TranslationApp.Services.Ocr;

namespace TranslationApp.Views;

public partial class OcrSelectionOverlay : Window
{
    private readonly ScreenCaptureResult _capture;
    private readonly TaskCompletionSource<Rectangle?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private System.Windows.Point _start;
    private bool _dragging;

    public OcrSelectionOverlay(ScreenCaptureResult capture)
    {
        _capture = capture;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => _completion.TrySetResult(null);
    }

    public Task<Rectangle?> SelectAsync()
    {
        Show();
        Activate();
        return _completion.Task;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var bounds = _capture.ScreenBounds;
        var dpi = VisualTreeHelper.GetDpi(this);
        Width = bounds.Width / dpi.DpiScaleX;
        Height = bounds.Height / dpi.DpiScaleY;
        SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x0040);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(Surface);
        _dragging = true;
        CaptureMouse();
        SelectionBox.Visibility = Visibility.Visible;
        UpdateBox(_start);
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragging) UpdateBox(e.GetPosition(Surface));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        var end = e.GetPosition(Surface);
        var dpi = VisualTreeHelper.GetDpi(this);
        var x = (int)Math.Round(Math.Min(_start.X, end.X) * dpi.DpiScaleX);
        var y = (int)Math.Round(Math.Min(_start.Y, end.Y) * dpi.DpiScaleY);
        var width = (int)Math.Round(Math.Abs(end.X - _start.X) * dpi.DpiScaleX);
        var height = (int)Math.Round(Math.Abs(end.Y - _start.Y) * dpi.DpiScaleY);
        var result = Rectangle.Intersect(new Rectangle(x, y, width, height), new Rectangle(0, 0, _capture.Bitmap.Width, _capture.Bitmap.Height));
        _completion.TrySetResult(result.Width >= 5 && result.Height >= 5 ? result : null);
        Close();
    }

    private void UpdateBox(System.Windows.Point current)
    {
        var x = Math.Min(_start.X, current.X);
        var y = Math.Min(_start.Y, current.Y);
        SelectionBox.Width = Math.Abs(current.X - _start.X);
        SelectionBox.Height = Math.Abs(current.Y - _start.Y);
        System.Windows.Controls.Canvas.SetLeft(SelectionBox, x);
        System.Windows.Controls.Canvas.SetTop(SelectionBox, y);
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        _completion.TrySetResult(null);
        Close();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
