using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using TranslationApp.Models;
using TranslationApp.Services.Documents;
using TranslationApp.ViewModels;

namespace TranslationApp.Views;

public partial class OcrSourceWindow : Window
{
    private bool _initialized;

    public OcrSourceWindow(OcrSourceViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Editor.Text = viewModel.Document.Text;
        Controller = new OcrDocumentController(viewModel.Document, Editor);
        _initialized = true;
        Closing += OnClosing;
    }

    public OcrSourceViewModel ViewModel => (OcrSourceViewModel)DataContext;
    public OcrDocumentController Controller { get; }
    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    public bool TryGetSelection(out string text, out int start, out int length)
    {
        start = Editor.SelectionStart;
        length = Editor.SelectionLength;
        text = length > 0 ? Editor.SelectedText : string.Empty;
        return length > 0 && !string.IsNullOrWhiteSpace(text);
    }

    public System.Drawing.Rectangle? GetSelectionScreenBounds()
    {
        if (Editor.SelectionLength <= 0) return null;
        var startRect = Editor.GetRectFromCharacterIndex(Editor.SelectionStart, true);
        var endRect = Editor.GetRectFromCharacterIndex(Editor.SelectionStart + Editor.SelectionLength, true);
        var topLeft = Editor.PointToScreen(new System.Windows.Point(startRect.Left, startRect.Top));
        var bottomRight = Editor.PointToScreen(new System.Windows.Point(Math.Max(startRect.Right, endRect.Right), endRect.Bottom));
        return System.Drawing.Rectangle.FromLTRB((int)topLeft.X, (int)topLeft.Y, (int)bottomRight.X, (int)bottomRight.Y);
    }

    private void Editor_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized) return;
        var changes = e.Changes.Select(x => new DocumentChange(x.Offset, x.RemovedLength, x.AddedLength)).ToList();
        Controller.OnTextChanged(changes);
    }

    private void OnClosing(object? sender, CancelEventArgs e) => Controller.DetachAll();
}
