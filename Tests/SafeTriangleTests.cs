using System.Drawing;
using SmartWindowTool.Core;
using Xunit;

namespace SmartWindowTool.Tests;

public class SafeTriangleTests
{
    // 布局近似真实菜单（物理像素）：触发按钮在左，弹窗与按钮右边相邻、向下延伸
    private static readonly Rectangle Anchor = new(200, 300, 260, 30);  // 触发按钮
    private static readonly Rectangle PopupRight = new(460, 300, 288, 420); // 右侧弹窗（与按钮右边相接）

    [Fact]
    public void PickNearEdge_PopupRightOfAnchor_ReturnsLeftEdge()
    {
        (Point a, Point b) = SafeTriangleBuilder.PickNearEdge(PopupRight, Anchor);

        Assert.Equal(new Point(460, 300), a);
        Assert.Equal(new Point(460, 720), b);
    }

    [Fact]
    public void PickNearEdge_PopupLeftOfAnchor_ReturnsRightEdge()
    {
        var popup = new Rectangle(0, 300, 192, 420); // 在按钮左侧

        (Point a, Point b) = SafeTriangleBuilder.PickNearEdge(popup, Anchor);

        Assert.Equal(new Point(192, 300), a);
        Assert.Equal(new Point(192, 720), b);
    }

    [Fact]
    public void PickNearEdge_PopupBelowAnchor_ReturnsTopEdge()
    {
        var popup = new Rectangle(200, 340, 260, 200); // 在按钮下方

        (Point a, Point b) = SafeTriangleBuilder.PickNearEdge(popup, Anchor);

        Assert.Equal(new Point(200, 340), a);
        Assert.Equal(new Point(460, 340), b);
    }

    [Fact]
    public void PickNearEdge_PopupAboveAnchor_ReturnsBottomEdge()
    {
        var popup = new Rectangle(200, 100, 260, 190); // 在按钮上方

        (Point a, Point b) = SafeTriangleBuilder.PickNearEdge(popup, Anchor);

        Assert.Equal(new Point(200, 290), a);
        Assert.Equal(new Point(460, 290), b);
    }

    [Fact]
    public void PickNearEdge_OverlappingRects_FallsBackToNearestEdge()
    {
        var popup = new Rectangle(250, 280, 200, 200); // 与按钮重叠
        Point center = new(330, 315);                  // 按钮中心

        (Point a, Point b) = SafeTriangleBuilder.PickNearEdge(popup, Anchor);

        // 重叠时退化为离锚点中心最近的边：上边缘 y=280 距中心 35，左边缘 x=250 距 80，其余更远
        Assert.Equal(new Point(250, 280), a);
        Assert.Equal(new Point(450, 280), b);
        Assert.True(SafeTriangleBuilder.DistanceSquaredToSegment(center, a, b) <
                    SafeTriangleBuilder.DistanceSquaredToSegment(center, new Point(250, 280), new Point(250, 480)));
    }

    [Fact]
    public void Contains_InteriorEdgeAndVertex_Inside_FarOutside_Outside()
    {
        var triangle = new SafeTriangle(new Point(0, 0), new Point(100, 0), new Point(0, 100));

        Assert.True(triangle.Contains(new Point(10, 10)));   // 内部
        Assert.True(triangle.Contains(new Point(50, 0)));    // 底边上
        Assert.True(triangle.Contains(new Point(0, 0)));     // 顶点
        Assert.True(triangle.Contains(new Point(50, 50)));   // 斜边上
        Assert.False(triangle.Contains(new Point(60, 60)));  // 斜边外
        Assert.False(triangle.Contains(new Point(-5, -5)));  // 远离
    }

    [Fact]
    public void Build_CoversDiagonalPathThroughGapBelowButton()
    {
        // 退出点：按钮下边缘中部（最后在按钮内的位置）
        var exitPoint = new Point(330, 329);
        var triangle = SafeTriangleBuilder.Build(exitPoint, PopupRight, Anchor);
        Assert.NotNull(triangle);

        // 斜向目标：弹窗左侧偏下（按钮与弹窗之间缝隙右下的路径中点）
        Assert.True(triangle!.Value.Contains(new Point(400, 400)));  // 缝隙中：按钮下方、弹窗左侧
        Assert.True(triangle!.Value.Contains(new Point(450, 500)));  // 弹窗左边缘附近
        Assert.True(triangle!.Value.Contains(new Point(380, 360)));  // 对角线路径上
    }

    [Fact]
    public void Build_PointMovingAwayFromPopup_IsOutside()
    {
        var exitPoint = new Point(330, 329);
        var triangle = SafeTriangleBuilder.Build(exitPoint, PopupRight, Anchor);
        Assert.NotNull(triangle);

        Assert.False(triangle!.Value.Contains(new Point(330, 500))); // 正下方远离弹窗
        Assert.False(triangle!.Value.Contains(new Point(330, 200))); // 正上方
        Assert.False(triangle!.Value.Contains(new Point(100, 400))); // 左侧
    }

    [Fact]
    public void Build_UsesExitPointAsApex()
    {
        var exitPoint = new Point(210, 328);
        var triangle = SafeTriangleBuilder.Build(exitPoint, PopupRight, Anchor);

        Assert.NotNull(triangle);
        Assert.Equal(exitPoint, triangle!.Value.Apex);
    }

    [Fact]
    public void Build_PadsBaseByTwiceAnchorHeight()
    {
        var triangle = SafeTriangleBuilder.Build(new Point(330, 329), PopupRight, Anchor);
        Assert.NotNull(triangle);

        // 锚点高 30 → 外扩 60：近边 (460,300)-(460,720) → (460,240)-(460,780)
        Assert.Equal(new Point(460, 240), triangle!.Value.BaseA);
        Assert.Equal(new Point(460, 780), triangle!.Value.BaseB);
    }

    [Fact]
    public void Build_ShortPopup_CoversDipBelowButton()
    {
        // 显示器弹窗场景：弹窗很矮（64px），几乎不比按钮低多少
        var shortPopup = new Rectangle(460, 300, 288, 64);
        var exitPoint = new Point(330, 329); // 按钮下边缘中部

        var triangle = SafeTriangleBuilder.Build(exitPoint, shortPopup, Anchor);
        Assert.NotNull(triangle);

        // 按钮下方绕行后再斜向弹窗的路径点，必须落在安全三角内
        Assert.True(triangle!.Value.Contains(new Point(400, 352)));
        Assert.True(triangle!.Value.Contains(new Point(380, 345)));
        // 明显偏离（弹窗下方太远）仍应关闭
        Assert.False(triangle!.Value.Contains(new Point(400, 430)));
    }

    [Fact]
    public void Build_EmptyPopupRect_ReturnsNull()
    {
        Assert.Null(SafeTriangleBuilder.Build(new Point(330, 329), Rectangle.Empty, Anchor));
    }

    [Fact]
    public void Build_PopupFlippedLeftNearScreenEdge_UsesRightEdge()
    {
        // 弹窗翻转到按钮左侧（菜单靠近屏幕右缘时 WPF 会翻转）
        var popup = new Rectangle(0, 300, 192, 420);
        var exitPoint = new Point(205, 329);

        var triangle = SafeTriangleBuilder.Build(exitPoint, popup, Anchor);
        Assert.NotNull(triangle);

        // 顶点在按钮左缘，底边是弹窗右缘 x=192 → 三角覆盖按钮与弹窗之间的缝隙
        Assert.True(triangle!.Value.Contains(new Point(196, 320)));
        Assert.False(triangle!.Value.Contains(new Point(400, 400))); // 弹窗另一侧（已越过弹窗）
    }
}
