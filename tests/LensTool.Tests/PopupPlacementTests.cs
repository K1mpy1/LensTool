using LensTool.Capture;
using LensTool.UI;

namespace LensTool.Tests;

public class PopupPlacementTests
{
    private static readonly PixelRect WorkArea = new(0, 0, 1920, 1040);

    [Fact]
    public void BelowRegionAlignedToItsLeftEdge()
    {
        var anchor = new PixelRect(200, 150, 300, 100);

        Assert.Equal(new PixelRect(200, 258, 400, 200), PopupPlacement.Place(anchor, WorkArea, 400, 200, gap: 8));
    }

    [Fact]
    public void AboveWhenNoRoomBelow()
    {
        var anchor = new PixelRect(200, 900, 300, 100);

        Assert.Equal(new PixelRect(200, 692, 400, 200), PopupPlacement.Place(anchor, WorkArea, 400, 200, gap: 8));
    }

    [Fact]
    public void RightWhenRegionIsTallerThanFreeSpaceAboveAndBelow()
    {
        var anchor = new PixelRect(100, 100, 300, 850);

        Assert.Equal(new PixelRect(408, 100, 400, 200), PopupPlacement.Place(anchor, WorkArea, 400, 200, gap: 8));
    }

    [Fact]
    public void LeftWhenOnlyLeftIsFree()
    {
        var anchor = new PixelRect(1500, 100, 400, 850);

        Assert.Equal(new PixelRect(1092, 100, 400, 200), PopupPlacement.Place(anchor, WorkArea, 400, 200, gap: 8));
    }

    [Fact]
    public void ShiftedLeftToStayOnScreen()
    {
        var anchor = new PixelRect(1800, 150, 100, 50);

        Assert.Equal(new PixelRect(1520, 208, 400, 200), PopupPlacement.Place(anchor, WorkArea, 400, 200, gap: 8));
    }

    [Fact]
    public void OverlapsInsideWorkAreaWhenNothingFits()
    {
        var anchor = new PixelRect(0, 0, 1920, 1040);

        Assert.Equal(new PixelRect(0, 840, 400, 200), PopupPlacement.Place(anchor, WorkArea, 400, 200));
    }

    [Fact]
    public void SecondMonitorWithNegativeCoordinates()
    {
        var workArea = new PixelRect(-2560, -200, 2560, 1400);
        var anchor = new PixelRect(-600, 900, 500, 200);

        // Too low for below (900 + 200 + 400 > 1200), so above.
        Assert.Equal(new PixelRect(-600, 500, 600, 400), PopupPlacement.Place(anchor, workArea, 600, 400));
    }

    [Fact]
    public void PopupLargerThanWorkAreaIsShrunk()
    {
        var anchor = new PixelRect(10, 10, 50, 50);

        var rect = PopupPlacement.Place(anchor, WorkArea, 3000, 2000);

        Assert.Equal(WorkArea.Width, rect.Width);
        Assert.Equal(WorkArea.Height, rect.Height);
        Assert.Equal(WorkArea, rect.Intersect(WorkArea));
    }
}
