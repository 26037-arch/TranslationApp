using System.Drawing;
using System.Windows.Automation;
using System.Windows.Forms;
using TranslationApp.Services.Logging;

namespace TranslationApp.Services.UiAutomation;

public sealed class SelectionBoundsService(AppLogger logger)
{
    public Rectangle GetSelectionOrCursor(IntPtr foregroundWindow)
    {
        try
        {
            if (foregroundWindow != IntPtr.Zero)
            {
                var element = AutomationElement.FromHandle(foregroundWindow);
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out var rawPattern) && rawPattern is TextPattern pattern)
                {
                    var selection = pattern.GetSelection();
                    var rectangles = selection.SelectMany(x => ToRectangles(x.GetBoundingRectangles())).Where(x => x.Width > 0 && x.Height > 0).ToList();
                    if (rectangles.Count > 0) return rectangles.Aggregate(Rectangle.Union);
                }
            }
        }
        catch (Exception ex) { logger.Error("UI Automation 선택 영역 조회 실패", ex); }
        var cursor = Cursor.Position;
        return new Rectangle(cursor.X, cursor.Y, 2, 2);
    }

    private static IEnumerable<Rectangle> ToRectangles(System.Windows.Rect[] values)
    {
        foreach (var value in values)
            yield return new Rectangle((int)Math.Round(value.X), (int)Math.Round(value.Y),
                (int)Math.Round(value.Width), (int)Math.Round(value.Height));
    }
}
