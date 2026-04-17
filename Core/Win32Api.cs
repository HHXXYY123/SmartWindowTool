using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace SmartWindowTool.Core
{
    public static class Win32Api
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetParent(IntPtr hwnd);

        public const uint GA_ROOT = 2;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        
        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        public const int GWL_EXSTYLE = -20;
        public const uint WS_EX_TOPMOST = 0x0008;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

        [DllImport("user32.dll")]
        public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll")]
        public static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint pcrKey, out byte pbAlpha, out uint pdwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PrintWindow(IntPtr hwnd, IntPtr hDC, uint nFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        public static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hWnd);

        public const int GWL_STYLE = -16;
        public const uint WS_EX_LAYERED = 0x00080000;
        public const uint WS_EX_TRANSPARENT = 0x00000020;
        public const uint LWA_ALPHA = 0x00000002;
        
        public const uint WS_CAPTION = 0x00C00000;
        public const uint WS_THICKFRAME = 0x00040000;
        public const uint SWP_FRAMECHANGED = 0x0020;
        
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint SWP_HIDEWINDOW = 0x0080;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_ASYNCWINDOWPOS = 0x4000;

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        public const int SW_SHOW = 5;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, int wParam, int lParam);
        public const uint WM_GETICON = 0x007F;
        public const int ICON_SMALL = 0;
        public const int ICON_BIG = 1;
        public const int ICON_SMALL2 = 2;

        [DllImport("user32.dll", EntryPoint = "GetClassLong")]
        public static extern IntPtr GetClassLongPtr32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetClassLongPtr")]
        public static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);

        public static IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 4) return GetClassLongPtr32(hWnd, nIndex);
            return GetClassLongPtr64(hWnd, nIndex);
        }
        
        public const int GCLP_HICON = -14;
        public const int GCLP_HICONSM = -34;

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        /// <summary>
        /// Gets the root window from the given point.
        /// </summary>
        public static IntPtr GetRootWindowFromCursor()
        {
            if (GetCursorPos(out POINT pt))
            {
                IntPtr hwnd = WindowFromPoint(pt);
                IntPtr rootHwnd = GetAncestor(hwnd, GA_ROOT);
                return rootHwnd != IntPtr.Zero ? rootHwnd : hwnd;
            }
            return IntPtr.Zero;
        }
    }
}