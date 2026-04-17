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
            if (_settings.EnableTransparencyWheel && _settings.TransparencyHotkey.IsModifierMatch(GetAsyncKeyState))
            {
                int delta = e.Delta > 0 ? 10 : -10;
                OnWindowTransparencyAdjustRequested?.Invoke(this, delta);
                e.Handled = true;
            }
            else if (_settings.EnableSizeWheel && _settings.HeightHotkey.IsModifierMatch(GetAsyncKeyState))
            {
                int delta = e.Delta > 0 ? 30 : -30;
                OnWindowHeightAdjustRequested?.Invoke(this, delta);
                e.Handled = true;
            }
            else if (_settings.EnableSizeWheel && _settings.WidthHotkey.IsModifierMatch(GetAsyncKeyState))
            {
                int delta = e.Delta > 0 ? 30 : -30;
                OnWindowWidthAdjustRequested?.Invoke(this, delta);
                e.Handled = true;
            }
        }

        private void GlobalHook_KeyDown(object sender, KeyEventArgs e)
        {
            // Check if user configured a keyboard-only shortcut
            var menuHotkey = _settings.MenuHotkey;
            var requiredButton = menuHotkey.GetParsedMouseButton();
            var requiredKey = menuHotkey.GetParsedKey();

            if (requiredButton == MouseButtons.None && requiredKey != Keys.None)
            {
                if (e.KeyCode == requiredKey && menuHotkey.IsModifierMatch(GetAsyncKeyState))
                {
                    // Trigger menu at current mouse position
                    var pos = System.Windows.Forms.Cursor.Position;
                    TriggerContextMenu(pos.X, pos.Y, null);
                    e.Handled = true;
                    return;
                }
            }

            bool isCtrlDown = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            bool isAltDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

            // Alt + Numpad (1-9) for Alignment
            if (_settings.EnableNumpadAlign && isAltDown && !isCtrlDown && e.KeyCode >= Keys.NumPad1 && e.KeyCode <= Keys.NumPad9)
            {
                WindowAlignment alignment = (WindowAlignment)(e.KeyCode - Keys.NumPad0);
                OnWindowAlignmentRequested?.Invoke(this, alignment);
                e.Handled = true;
            }
            // Ctrl + Numpad (0-9) for Transparency
            else if (_settings.EnableNumpadMove && isCtrlDown && !isAltDown && e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9)
            {
                int level = e.KeyCode - Keys.NumPad0;
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
            var requiredButton = _settings.MenuHotkey.GetParsedMouseButton();

            // Ignore simulated clicks (e.g. from Everywhere or other hotkey tools)
            // By checking if the physical mouse button is actually held down
            bool isPhysicalButtonDown = false;
            if (requiredButton == MouseButtons.Right) isPhysicalButtonDown = (GetAsyncKeyState(0x02) & 0x8000) != 0;
            else if (requiredButton == MouseButtons.Left) isPhysicalButtonDown = (GetAsyncKeyState(0x01) & 0x8000) != 0;
            else if (requiredButton == MouseButtons.Middle) isPhysicalButtonDown = (GetAsyncKeyState(0x04) & 0x8000) != 0;
            else if (requiredButton == MouseButtons.XButton1) isPhysicalButtonDown = (GetAsyncKeyState(0x05) & 0x8000) != 0;
            else if (requiredButton == MouseButtons.XButton2) isPhysicalButtonDown = (GetAsyncKeyState(0x06) & 0x8000) != 0;

            // Only trigger via mouse if a specific mouse button is actually required
            if (requiredButton != MouseButtons.None && 
                e.Button == requiredButton && 
                isPhysicalButtonDown &&
                !IsAnyNonModifierKeyPressed() &&
                _settings.MenuHotkey.IsModifierMatch(GetAsyncKeyState))
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