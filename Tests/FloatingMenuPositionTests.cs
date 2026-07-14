using System.Drawing;
using Xunit;

namespace SmartWindowTool.Tests;

public class FloatingMenuPositionTests
{
    [Fact]
    public void ClampFloatingMenuPosition_KeepsMenuInsideBottomRightEdges()
    {
        var area = new Rectangle(0, 0, 1920, 1080);

        var position = MainWindow.ClampFloatingMenuPosition(area, 1800, 1000, 300, 400);

        Assert.Equal((1620, 680), position);
    }

    [Fact]
    public void ClampFloatingMenuPosition_PreservesPositionWhenMenuFits()
    {
        var area = new Rectangle(0, 0, 1920, 1080);

        var position = MainWindow.ClampFloatingMenuPosition(area, 500, 300, 300, 400);

        Assert.Equal((500, 300), position);
    }

    [Fact]
    public void ClampFloatingMenuPosition_UsesAreaOriginWhenMenuIsLargerThanArea()
    {
        var area = new Rectangle(-1280, 100, 1280, 720);

        var position = MainWindow.ClampFloatingMenuPosition(area, -100, 700, 1600, 900);

        Assert.Equal((-1280, 100), position);
    }
}
