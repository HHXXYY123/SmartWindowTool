using System;
using System.Windows.Forms;
using Gma.System.MouseKeyHook;
using System.Diagnostics;

namespace SmartWindowTool.Core
{
    public enum WindowAlignment
    {
        BottomLeft = 1, BottomCenter = 2, BottomRight = 3,
        MiddleLeft = 4, Center = 5, MiddleRight = 6,
        TopLeft = 7, TopCenter = 8, TopRight = 9
    }

    public class HookService : IDisposable
    {
        private IKeyboardMouseEvents _globalHook;
        private Models.AppSettings _settings;
        
        // Event triggered when Ctrl + Right Click is pressed
        public event EventHandler<HookEventArgs> OnContextMenuRequested;
        public event EventHandler<WindowAlignment> OnWindowAlignmentRequested;
        public event EventHandler<int> OnWindowTransparencyRequested;
        public event EventHandler<int> OnWindowTransparencyAdjustRequested;
        public event EventHandler<MouseEventExtArgs> OnAnyMouseDown;

        public HookService(Models.AppSettings settings)
        {
            _settings = settings;
        }

        public void Start()
        {
            _globalHook = Hook.GlobalEvents();
            _globalHook.MouseDownExt += GlobalHook_MouseDownExt;
            _globalHook.KeyDown += GlobalHook_KeyDown;
            _globalHook.MouseWheelExt += GlobalHook_MouseWheelExt;
        }

        private void GlobalHook_MouseWheelExt(object sender, MouseEventExtArgs e)
        {
            bool isCtrlDown = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            bool isShiftDown = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;

            if (isCtrlDown && isShiftDown)
            {
                // e.Delta is usually 120 or -120
                int delta = e.Delta > 0 ? 10 : -10;
                OnWindowTransparencyAdjustRequested?.Invoke(this, delta);
                e.Handled = true;
            }
        }

        private void GlobalHook_KeyDown(object sender, KeyEventArgs e)
        {
            bool isCtrlDown = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            bool isAltDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

            // Alt + Numpad (1-9) for Alignment
            if (isAltDown && !isCtrlDown && e.KeyCode >= Keys.NumPad1 && e.KeyCode <= Keys.NumPad9)
            {
                WindowAlignment alignment = (WindowAlignment)(e.KeyCode - Keys.NumPad0);
                OnWindowAlignmentRequested?.Invoke(this, alignment);
                e.Handled = true;
            }
            // Ctrl + Numpad (0-9) for Transparency
            else if (isCtrlDown && !isAltDown && e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9)
            {
                int level = e.KeyCode - Keys.NumPad0;
                // SmartContextMenu behavior:
                // Numpad 0: 100% opaque
                // Numpad 1: 10% transparency (90% opaque)
                // Numpad 2: 20% transparency (80% opaque)
                // ...
                // Numpad 9: 90% transparency (10% opaque)
                int transparency = level == 0 ? 100 : (100 - (level * 10));
                OnWindowTransparencyRequested?.Invoke(this, transparency);
                e.Handled = true;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        private const int VK_CONTROL = 0x11;
        private const int VK_SHIFT = 0x10;
        private const int VK_MENU = 0x12; // Alt

        private void GlobalHook_MouseDownExt(object sender, MouseEventExtArgs e)
        {
            bool isCtrlDown = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            bool isShiftDown = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
            bool isAltDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

            if (e.Button == _settings.GetParsedMouseButton() && 
                isCtrlDown == _settings.RequireCtrl &&
                isShiftDown == _settings.RequireShift &&
                isAltDown == _settings.RequireAlt)
            {
                IntPtr target = Win32Api.GetRootWindowFromCursor();
                if (target != IntPtr.Zero)
                {
                    Win32Api.GetWindowThreadProcessId(target, out uint processId);
                    try
                    {
                        var process = System.Diagnostics.Process.GetProcessById((int)processId);
                        string procName = process.ProcessName + ".exe";
                        foreach (var blacklisted in _settings.BlacklistProcesses)
                        {
                            if (procName.Equals(blacklisted, StringComparison.OrdinalIgnoreCase))
                            {
                                // Ignore hotkey for blacklisted process
                                return;
                            }
                        }
                    }
                    catch { }
                }

                // Prevent the original right click from propagating
                e.Handled = true;
                
                Debug.WriteLine("Custom Hotkey Detected!");
                OnContextMenuRequested?.Invoke(this, new HookEventArgs(e.X, e.Y));
            }
            else
            {
                // If it's not the hotkey, we trigger the AnyMouseDown event (used to dismiss the menu)
                OnAnyMouseDown?.Invoke(this, e);
            }
        }

        public void Stop()
        {
            if (_globalHook != null)
            {
                _globalHook.MouseDownExt -= GlobalHook_MouseDownExt;
                _globalHook.KeyDown -= GlobalHook_KeyDown;
                _globalHook.MouseWheelExt -= GlobalHook_MouseWheelExt;
                _globalHook.Dispose();
                _globalHook = null;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }

    public class HookEventArgs : EventArgs
    {
        public int MouseX { get; set; }
        public int MouseY { get; set; }

        public HookEventArgs(int x, int y)
        {
            MouseX = x;
            MouseY = y;
        }
    }
}