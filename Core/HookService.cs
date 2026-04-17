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
        public event EventHandler<int> OnWindowHeightAdjustRequested;
        public event EventHandler<int> OnWindowWidthAdjustRequested;
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
            bool isAltDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

            if (isCtrlDown && isShiftDown && !isAltDown)
            {
                // Ctrl + Shift + Wheel -> Transparency
                int delta = e.Delta > 0 ? 10 : -10;
                OnWindowTransparencyAdjustRequested?.Invoke(this, delta);
                e.Handled = true;
            }
            else if (isCtrlDown && isAltDown && !isShiftDown)
            {
                // Ctrl + Alt + Wheel -> Height
                int delta = e.Delta > 0 ? 30 : -30;
                OnWindowHeightAdjustRequested?.Invoke(this, delta);
                e.Handled = true;
            }
            else if (isShiftDown && isAltDown && !isCtrlDown)
            {
                // Shift + Alt + Wheel -> Width
                int delta = e.Delta > 0 ? 30 : -30;
                OnWindowWidthAdjustRequested?.Invoke(this, delta);
                e.Handled = true;
            }
        }

        private void GlobalHook_KeyDown(object sender, KeyEventArgs e)
        {
            bool isCtrlDown = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            bool isAltDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
            bool isShiftDown = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;

            // Check if user configured a keyboard-only shortcut (Mouse button set to None)
            var requiredButton = _settings.GetParsedMouseButton();
            if (requiredButton == MouseButtons.None)
            {
                // In keyboard-only mode, we trigger when the exact modifiers are met AND a specific key is pressed.
                // Currently, we'll map "None" mouse button + modifiers + "S" key as the trigger to match user expectation
                if (e.KeyCode == Keys.S && 
                    isCtrlDown == _settings.RequireCtrl && 
                    isShiftDown == _settings.RequireShift && 
                    isAltDown == _settings.RequireAlt)
                {
                    // Trigger menu at current mouse position
                    var pos = System.Windows.Forms.Cursor.Position;
                    TriggerContextMenu(pos.X, pos.Y, null);
                    e.Handled = true;
                    return;
                }
            }

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

        private bool IsAnyNonModifierKeyPressed()
        {
            // Check A-Z (0x41 - 0x5A)
            for (int i = 0x41; i <= 0x5A; i++)
            {
                if ((GetAsyncKeyState(i) & 0x8000) != 0) return true;
            }
            // Check 0-9 (0x30 - 0x39)
            for (int i = 0x30; i <= 0x39; i++)
            {
                if ((GetAsyncKeyState(i) & 0x8000) != 0) return true;
            }
            // Check F1-F12 (0x70 - 0x7B)
            for (int i = 0x70; i <= 0x7B; i++)
            {
                if ((GetAsyncKeyState(i) & 0x8000) != 0) return true;
            }
            return false;
        }

        private void GlobalHook_MouseDownExt(object sender, MouseEventExtArgs e)
        {
            bool isCtrlDown = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            bool isShiftDown = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
            bool isAltDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
            
            var requiredButton = _settings.GetParsedMouseButton();

            // Ignore simulated clicks (e.g. from Everywhere or other hotkey tools)
            // By checking if the physical mouse button is actually held down
            bool isPhysicalButtonDown = false;
            if (requiredButton == MouseButtons.Right) isPhysicalButtonDown = (GetAsyncKeyState(0x02) & 0x8000) != 0;
            else if (requiredButton == MouseButtons.Left) isPhysicalButtonDown = (GetAsyncKeyState(0x01) & 0x8000) != 0;
            else if (requiredButton == MouseButtons.Middle) isPhysicalButtonDown = (GetAsyncKeyState(0x04) & 0x8000) != 0;

            // Only trigger via mouse if a specific mouse button is actually required
            if (requiredButton != MouseButtons.None && 
                e.Button == requiredButton && 
                isPhysicalButtonDown &&
                !IsAnyNonModifierKeyPressed() &&
                isCtrlDown == _settings.RequireCtrl &&
                isShiftDown == _settings.RequireShift &&
                isAltDown == _settings.RequireAlt)
            {
                TriggerContextMenu(e.X, e.Y, e);
            }
            else if (requiredButton != MouseButtons.None)
            {
                // If it's not the hotkey and we aren't in keyboard-only mode, we trigger the AnyMouseDown event (used to dismiss the menu)
                OnAnyMouseDown?.Invoke(this, e);
            }
            else if (requiredButton == MouseButtons.None)
            {
                // In keyboard-only mode, we still need to dismiss the menu when user clicks anywhere
                OnAnyMouseDown?.Invoke(this, e);
            }
        }

        private void TriggerContextMenu(int x, int y, MouseEventExtArgs e = null)
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

            // Prevent the original right click from propagating if triggered by mouse
            if (e != null)
            {
                e.Handled = true;
            }
            
            Debug.WriteLine("Custom Hotkey Detected!");
            OnContextMenuRequested?.Invoke(this, new HookEventArgs(x, y));
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