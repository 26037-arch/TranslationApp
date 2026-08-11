using TranslationApp.Views;

namespace TranslationApp.Services.Windows;

public sealed class WindowRegistry
{
    private readonly List<WeakReference<TranslationWindow>> _translationWindows = [];
    private readonly List<WeakReference<OcrSourceWindow>> _ocrWindows = [];

    public void Add(TranslationWindow window)
    {
        _translationWindows.Add(new(window));
        window.Closed += (_, _) => Cleanup();
    }

    public void Add(OcrSourceWindow window)
    {
        _ocrWindows.Add(new(window));
        window.Closed += (_, _) => Cleanup();
    }

    public IReadOnlyList<TranslationWindow> TranslationWindows => Alive(_translationWindows);
    public IReadOnlyList<OcrSourceWindow> OcrWindows => Alive(_ocrWindows);

    private void Cleanup()
    {
        _translationWindows.RemoveAll(x => !x.TryGetTarget(out _));
        _ocrWindows.RemoveAll(x => !x.TryGetTarget(out _));
    }

    private static IReadOnlyList<T> Alive<T>(List<WeakReference<T>> references) where T : class =>
        references.Select(x => x.TryGetTarget(out var value) ? value : null).Where(x => x is not null).Cast<T>().ToList();
}
