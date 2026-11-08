using System;
using System.Drawing;

namespace SmartWindowTool.Core
{
    /// <summary>
    /// 菜单悬停安全三角：鼠标离开触发按钮/弹窗后，以退出点为顶点、弹窗近边为底边的扇形区域。
    /// 鼠标沿对角线移向弹窗途中落在三角内时视为仍在菜单路径上，不收起弹窗；离开三角才关闭。
    /// 全部使用物理屏幕像素（与全局钩子、Win32 矩形同一坐标系）。
    /// </summary>
    internal readonly struct SafeTriangle
    {
        public SafeTriangle(Point apex, Point baseA, Point baseB)
        {
            Apex = apex;
            BaseA = baseA;
            BaseB = baseB;
        }

        /// <summary>顶点：鼠标最后停留在按钮/弹窗区域内的位置。</summary>
        public Point Apex { get; }

        /// <summary>底边端点 A：弹窗近边的一端。</summary>
        public Point BaseA { get; }

        /// <summary>底边端点 B：弹窗近边的另一端。</summary>
        public Point BaseB { get; }

        /// <summary>点是否落在三角内（含边界）。</summary>
        public bool Contains(Point point)
        {
            return ContainsPoint(point, Apex, BaseA, BaseB);
        }

        /// <summary>重心坐标法判断点是否在三角形内（含边上/顶点）。</summary>
        internal static bool ContainsPoint(Point p, Point a, Point b, Point c)
        {
            double d1 = Sign(p, a, b);
            double d2 = Sign(p, b, c);
            double d3 = Sign(p, c, a);
            bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
            bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(hasNeg && hasPos);
        }

        private static double Sign(Point p1, Point p2, Point p3)
        {
            return (p1.X - p3.X) * (double)(p2.Y - p3.Y) - (p2.X - p3.X) * (double)(p1.Y - p3.Y);
        }
    }

    internal static class SafeTriangleBuilder
    {
        /// <summary>
        /// 构建安全三角：顶点为退出点，底边为弹窗上朝向锚点（触发按钮）的近边，
        /// 并向两端各外扩 2 倍锚点高度，覆盖「按钮下方绕行再斜向弹窗」的滑动路径。
        /// 弹窗尚未布局（宽高为 0）时返回 null。
        /// </summary>
        internal static SafeTriangle? Build(Point exitPoint, Rectangle popupRect, Rectangle anchorRect)
        {
            if (popupRect.Width <= 0 || popupRect.Height <= 0)
            {
                return null;
            }
            (Point a, Point b) = PickNearEdge(popupRect, anchorRect);

            // 外扩底边：短弹窗（如显示器列表）的近边太短，三角盖不住按钮下方的绕行路径
            int pad = Math.Max(24, anchorRect.Height * 2);
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len > 0)
            {
                dx /= len;
                dy /= len;
                a = new Point((int)Math.Round(a.X - dx * pad), (int)Math.Round(a.Y - dy * pad));
                b = new Point((int)Math.Round(b.X + dx * pad), (int)Math.Round(b.Y + dy * pad));
            }
            return new SafeTriangle(exitPoint, a, b);
        }

        /// <summary>
        /// 选择弹窗上朝向锚点的近边：按两矩形的相对方位（右/左/下/上）判断；
        /// 重叠等无法判断方位时退化为离锚点中心最近的边。
        /// </summary>
        internal static (Point A, Point B) PickNearEdge(Rectangle popupRect, Rectangle anchorRect)
        {
            if (popupRect.Left >= anchorRect.Right)
            {
                return (new Point(popupRect.Left, popupRect.Top), new Point(popupRect.Left, popupRect.Bottom));
            }
            if (popupRect.Right <= anchorRect.Left)
            {
                return (new Point(popupRect.Right, popupRect.Top), new Point(popupRect.Right, popupRect.Bottom));
            }
            if (popupRect.Top >= anchorRect.Bottom)
            {
                return (new Point(popupRect.Left, popupRect.Top), new Point(popupRect.Right, popupRect.Top));
            }
            if (popupRect.Bottom <= anchorRect.Top)
            {
                return (new Point(popupRect.Left, popupRect.Bottom), new Point(popupRect.Right, popupRect.Bottom));
            }

            // 重叠：退化为离锚点中心最近的边
            Point center = new(
                anchorRect.Left + anchorRect.Width / 2,
                anchorRect.Top + anchorRect.Height / 2);
            var leftEdge = (A: new Point(popupRect.Left, popupRect.Top), B: new Point(popupRect.Left, popupRect.Bottom));
            var rightEdge = (A: new Point(popupRect.Right, popupRect.Top), B: new Point(popupRect.Right, popupRect.Bottom));
            var topEdge = (A: new Point(popupRect.Left, popupRect.Top), B: new Point(popupRect.Right, popupRect.Top));
            var bottomEdge = (A: new Point(popupRect.Left, popupRect.Bottom), B: new Point(popupRect.Right, popupRect.Bottom));

            (Point A, Point B) best = leftEdge;
            double bestDist = DistanceSquaredToSegment(center, leftEdge.A, leftEdge.B);
            foreach (var edge in new[] { rightEdge, topEdge, bottomEdge })
            {
                double dist = DistanceSquaredToSegment(center, edge.A, edge.B);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = edge;
                }
            }
            return best;
        }

        internal static double DistanceSquaredToSegment(Point p, Point a, Point b)
        {
            double abX = b.X - a.X;
            double abY = b.Y - a.Y;
            double apX = p.X - a.X;
            double apY = p.Y - a.Y;
            double abLenSquared = abX * abX + abY * abY;
            double t = abLenSquared <= 0 ? 0 : (apX * abX + apY * abY) / abLenSquared;
            t = Math.Clamp(t, 0, 1);
            double dx = apX - t * abX;
            double dy = apY - t * abY;
            return dx * dx + dy * dy;
        }
    }
}
