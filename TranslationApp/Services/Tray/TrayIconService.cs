using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using TranslationApp.Services.Translation;

namespace TranslationApp.Services.Tray;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly ITranslationEngineLifecycle _engine;

    public TrayIconService(ITranslationEngineLifecycle engine, Action openSettings, Action captureOcr, Action exit)
    {
        _engine = engine;
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
        _engine.StateChanged += EngineOnStateChanged;
        EngineOnStateChanged(this, EventArgs.Empty);
    }

    private void EngineOnStateChanged(object? sender, EventArgs e)
    {
        void Update()
        {
            var text = $"TranslationApp - {_engine.StatusMessage}";
            _icon.Text = text.Length <= 63 ? text : text[..63];
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) Update();
        else dispatcher.BeginInvoke((Action)Update);
    }

    private static void Dispatch(Action action) => System.Windows.Application.Current.Dispatcher.BeginInvoke(action);

    public void Dispose()
    {
        _engine.StateChanged -= EngineOnStateChanged;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
