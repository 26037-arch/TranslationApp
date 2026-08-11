using System.Drawing;

namespace TranslationApp.Services.Windows;

public static class WindowPlacementCalculator
{
    public static Rectangle Calculate(Rectangle anchor, Size desired, Rectangle workArea, IReadOnlyList<Rectangle> occupied)
    {
        const int gap = 10;
        var candidates = new[]
        {
            new Rectangle(anchor.Left, anchor.Bottom + gap, desired.Width, desired.Height),
            new Rectangle(anchor.Right + gap, anchor.Top, desired.Width, desired.Height),
            new Rectangle(anchor.Left, anchor.Top - desired.Height - gap, desired.Width, desired.Height),
            new Rectangle(anchor.Left - desired.Width - gap, anchor.Top, desired.Width, desired.Height)
        }.Select(x => Clamp(x, workArea)).Distinct().ToList();

        foreach (var candidate in candidates)
            if (!HasSevereOverlap(candidate, occupied)) return candidate;

        var current = candidates[0];
        for (var step = 1; step <= 12; step++)
        {
            var dx = (step % 3) * 28;
            var dy = step * 26;
            foreach (var sign in new[] { 1, -1 })
            {
                var shifted = Clamp(new Rectangle(current.X + dx * sign, current.Y + dy * sign, current.Width, current.Height), workArea);
                if (!HasSevereOverlap(shifted, occupied)) return shifted;
            }
        }
        return current;
    }

    public static Rectangle Clamp(Rectangle window, Rectangle workArea)
    {
        var width = Math.Min(window.Width, workArea.Width);
        var height = Math.Min(window.Height, workArea.Height);
        var x = Math.Clamp(window.X, workArea.Left, workArea.Right - width);
        var y = Math.Clamp(window.Y, workArea.Top, workArea.Bottom - height);
        return new Rectangle(x, y, width, height);
    }

    private static bool HasSevereOverlap(Rectangle candidate, IReadOnlyList<Rectangle> occupied)
    {
        var area = Math.Max(1, candidate.Width * candidate.Height);
        return occupied.Any(other =>
        {
            var overlap = Rectangle.Intersect(candidate, other);
            return overlap.Width * overlap.Height >= area * 0.45;
        });
    }
}
