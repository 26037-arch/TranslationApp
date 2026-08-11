using System.Windows;

namespace TranslationApp.Services.Clipboard;

public static class ClipboardSnapshotFactory
{
    public static ClipboardSnapshot Clone(System.Windows.IDataObject? source)
    {
        if (source is null) return new ClipboardSnapshot(null);
        var copy = new System.Windows.DataObject();
        foreach (var format in source.GetFormats(false))
        {
            try
            {
                var value = source.GetData(format, false);
                if (value is not null) copy.SetData(format, value, false);
            }
            catch { /* A delayed-rendering source may reject an individual format. */ }
        }
        return new ClipboardSnapshot(copy);
    }
}
