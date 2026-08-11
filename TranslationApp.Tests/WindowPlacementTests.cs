using System.Drawing;
using TranslationApp.Services.Windows;

namespace TranslationApp.Tests;

public sealed class WindowPlacementTests
{
    [Fact]
    public void PlacementIsClampedToWorkingArea()
    {
        var result = WindowPlacementCalculator.Calculate(new Rectangle(1900, 1070, 10, 10), new Size(430, 390),
            new Rectangle(0, 0, 1920, 1040), []);
        Assert.True(result.Left >= 0 && result.Top >= 0);
        Assert.True(result.Right <= 1920 && result.Bottom <= 1040);
    }

    [Fact]
    public void SevereOverlapMovesOnlyNewWindow()
    {
        var occupied = new[] { new Rectangle(100, 130, 430, 390) };
        var result = WindowPlacementCalculator.Calculate(new Rectangle(100, 100, 80, 20), new Size(430, 390),
            new Rectangle(0, 0, 1920, 1040), occupied);
        Assert.NotEqual(occupied[0], result);
    }
}
