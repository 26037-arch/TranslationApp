using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using TranslationApp.Services.Translation;

namespace TranslationApp.Services.Tray;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly IModelLifecycle _model;

    public TrayIconService(IModelLifecycle model, Action openSettings, Action captureOcr, Action exit)
    {
        _model = model;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("화면 OCR  (Ctrl+Alt+O)", null, (_, _) => Dispatch(captureOcr));
        menu.Items.Add("설정", null, (_, _) => Dispatch(openSettings));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => Dispatch(exit));
        _icon = new Forms.NotifyIcon
        {
            Icon = Drawing.SystemIcons.Application,
            Visible = true,
            Text = "TranslationApp",
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => Dispatch(openSettings);
        _model.StateChanged += ModelOnStateChanged;
        ModelOnStateChanged(this, EventArgs.Empty);
    }

    private void ModelOnStateChanged(object? sender, EventArgs e)
    {
        var text = $"TranslationApp - {_model.StatusMessage}";
        _icon.Text = text.Length <= 63 ? text : text[..63];
    }

    private static void Dispatch(Action action) => System.Windows.Application.Current.Dispatcher.BeginInvoke(action);

    public void Dispose()
    {
        _model.StateChanged -= ModelOnStateChanged;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
