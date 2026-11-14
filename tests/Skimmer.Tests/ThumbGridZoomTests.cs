using Xunit;

namespace Skimmer.Tests;

/// <summary>
/// Ctrl+колесо: превью меняют размер шагами, а не плавно. Колесо с высоким
/// разрешением и тачпады присылают доли «щелчка» (WHEEL_DELTA = 120), поэтому
/// остаток копится до целого шага — иначе масштаб дёргался бы на каждом событии.
/// </summary>
public class ThumbGridZoomTests
{
    private const int Notch = 120;

    [Fact]
    public void WholeNotch_IsOneStep()
    {
        int remainder = 0;

        Assert.Equal(1, ThumbGrid.WheelNotches(ref remainder, Notch));
        Assert.Equal(0, remainder);
        Assert.Equal(-1, ThumbGrid.WheelNotches(ref remainder, -Notch));
        Assert.Equal(0, remainder);
    }

    [Fact]
    public void SeveralNotchesInOneEvent_AreCountedAtOnce()
    {
        int remainder = 0;

        Assert.Equal(3, ThumbGrid.WheelNotches(ref remainder, Notch * 3));
        Assert.Equal(-2, ThumbGrid.WheelNotches(ref remainder, -Notch * 2));
        Assert.Equal(0, remainder);
    }

    [Fact]
    public void Fractions_AccumulateUpToAWholeNotch()
    {
        int remainder = 0;

        Assert.Equal(0, ThumbGrid.WheelNotches(ref remainder, 40));
        Assert.Equal(0, ThumbGrid.WheelNotches(ref remainder, 40));
        Assert.Equal(80, remainder);

        Assert.Equal(1, ThumbGrid.WheelNotches(ref remainder, 40));
        Assert.Equal(0, remainder);
    }

    [Fact]
    public void Fractions_AccumulateInNegativeDirection()
    {
        int remainder = 0;

        Assert.Equal(0, ThumbGrid.WheelNotches(ref remainder, -80));
        Assert.Equal(-1, ThumbGrid.WheelNotches(ref remainder, -50));
        Assert.Equal(-10, remainder);
    }

    [Fact]
    public void Remainder_CarriesOverBetweenEvents()
    {
        int remainder = 0;

        Assert.Equal(1, ThumbGrid.WheelNotches(ref remainder, 150));
        Assert.Equal(30, remainder);

        Assert.Equal(1, ThumbGrid.WheelNotches(ref remainder, 90));
        Assert.Equal(0, remainder);
    }

    [Fact]
    public void UpAndDown_AtTheSameSpeed()
    {
        int up = 0, down = 0;
        int upSum = 0, downSum = 0;
        for (int i = 0; i < 12; i++)
        {
            upSum += ThumbGrid.WheelNotches(ref up, 50);
            downSum += ThumbGrid.WheelNotches(ref down, -50);
        }

        Assert.Equal(-upSum, downSum);
        Assert.Equal(5, upSum);    // 12 * 50 = 600 = 5 notches
    }
}
