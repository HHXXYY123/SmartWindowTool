using System;
using System.Windows.Forms;
using Gma.System.MouseKeyHook;
using System.Diagnostics;
using System.Collections.Generic;
using SmartWindowTool.Models;

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
        private IKeyboardMouseEvents? _globalHook;
        private readonly Models.AppSettings _settings;
        
        // Event triggered when Context Menu is requested
        public event EventHandler<HookEventArgs>? OnContextMenuRequested;
        public event EventHandler<WindowAlignment>? OnWindowAlignmentRequested;
        public event EventHandler<int>? OnWindowTransparencyRequested;
        public event EventHandler<int>? OnWindowTransparencyAdjustRequested;
        public event EventHandler<int>? OnWindowHeightAdjustRequested;
        public event EventHandler<int>? OnWindowWidthAdjustRequested;
        public event EventHandler<(int DeltaX, int DeltaY)>? OnWindowPositionMoveRequested;
        public event EventHandler<MouseEventExtArgs>? OnAnyMouseDown;

        public HookService(Models.AppSettings settings)
        {
            _settings = settings;
        }

        public void Start()
        {
            if (_globalHook != null) return;
            _globalHook = Hook.GlobalEvents();
            _globalHook.MouseDownExt += GlobalHook_MouseDownExt;
            _globalHook.KeyDown += GlobalHook_KeyDown;
            _globalHook.MouseWheelExt += GlobalHook_MouseWheelExt;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        
        private const int VK_CONTROL = 0x11;
        private const int VK_SHIFT = 0x10;
        private const int VK_MENU = 0x12; // Alt
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        private bool IsKeyPressed(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        private int GetVkFromKeyName(string keyName)
        {
            switch (keyName)
            {
                case "Backspace": return 0x08;
                case "Tab": return 0x09;
                case "Enter": return 0x0D;
                case "Pause": return 0x13;
                case "Caps Lock": return 0x14;
                case "Esc": return 0x1B;
                case "Space": return 0x20;
                case "Page Up": return 0x21;
                case "Page Down": return 0x22;
                case "End": return 0x23;
                case "Home": return 0x24;
                case "Left Arrow": return 0x25;
                case "Up Arrow": return 0x26;
                case "Right Arrow": return 0x27;
                case "Down Arrow": return 0x28;
                case "Print Screen": return 0x2C;
                case "Ins": return 0x2D;
                case "Del": return 0x2E;
                case "0": return 0x30; case "1": return 0x31; case "2": return 0x32;
                case "3": return 0x33; case "4": return 0x34; case "5": return 0x35;
                case "6": return 0x36; case "7": return 0x37; case "8": return 0x38; case "9": return 0x39;
                case "A": return 0x41; case "B": return 0x42; case "C": return 0x43; case "D": return 0x44;
                case "E": return 0x45; case "F": return 0x46; case "G": return 0x47; case "H": return 0x48;
                case "I": return 0x49; case "J": return 0x4A; case "K": return 0x4B; case "L": return 0x4C;
                case "M": return 0x4D; case "N": return 0x4E; case "O": return 0x4F; case "P": return 0x50;
                case "Q": return 0x51; case "R": return 0x52; case "S": return 0x53; case "T": return 0x54;
                case "U": return 0x55; case "V": return 0x56; case "W": return 0x57; case "X": return 0x58;
                case "Y": return 0x59; case "Z": return 0x5A;
                case "F1": return 0x70; case "F2": return 0x71; case "F3": return 0x72; case "F4": return 0x73;
                case "F5": return 0x74; case "F6": return 0x75; case "F7": return 0x76; case "F8": return 0x77;
                case "F9": return 0x78; case "F10": return 0x79; case "F11": return 0x7A; case "F12": return 0x7B;
                default: return 0;
            }
        }

        private bool IsHotkeyMatch(HotkeyProfile profile, MouseButtons? mouseButton = null, bool isMouseWheel = false)
        {
            if (profile == null || !profile.IsEnabled) return false;

            if (mouseButton.HasValue)
            {
                if (profile.MouseButton != mouseButton.Value.ToString() && 
                    !(profile.MouseButton == "None" && mouseButton.Value == MouseButtons.None))
                    return false;
            }
            else if (!isMouseWheel)
            {
                if (profile.MouseButton != "None") return false;
            }

            var expectedMods = new HashSet<string>();
            if (profile.Modifier1 != "None") expectedMods.Add(profile.Modifier1);
            if (profile.Modifier2 != "None") expectedMods.Add(profile.Modifier2);
            
            // If no modifiers and no keys are expected, and it's a mouse wheel event, this is an empty hotkey that shouldn't trigger on every wheel scroll
            if (isMouseWheel && expectedMods.Count == 0 && profile.Key1 == "None" && profile.Key2 == "None")
            {
                return false;
            }

            if (expectedMods.Contains("Ctrl") != IsKeyPressed(VK_CONTROL)) return false;
            if (expectedMods.Contains("Shift") != IsKeyPressed(VK_SHIFT)) return false;
            if (expectedMods.Contains("Alt") != IsKeyPressed(VK_MENU)) return false;
            if (expectedMods.Contains("WinL") != IsKeyPressed(VK_LWIN)) return false;
            if (expectedMods.Contains("WinR") != IsKeyPressed(VK_RWIN)) return false;

            var expectedKeys = new HashSet<string>();
            if (profile.Key1 != "None") expectedKeys.Add(profile.Key1);
            if (profile.Key2 != "None") expectedKeys.Add(profile.Key2);

            foreach (var key in expectedKeys)
            {
                int vk = GetVkFromKeyName(key);
                if (vk != 0 && !IsKeyPressed(vk)) return false;
            }

            return true;
        }

        // --- 进程黑名单 ---

        // MainModule 查询有成本，滚轮会高频触发，这里按 PID 做 1 秒结果缓存。
        private uint _blacklistCachePid;
        private BlacklistMode? _blacklistCacheMode;
        private long _blacklistCacheTick;
        private const long BlacklistCacheDurationMs = 1000;

        /// <summary>
        /// 判断窗口所属进程是否命中黑名单。
        /// 条目不区分大小写，支持进程名（javaw.exe / javaw）或完整路径两种写法。
        /// </summary>
        internal bool IsWindowBlacklisted(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            Win32Api.GetWindowThreadProcessId(hwnd, out uint processId);
            if (processId == 0) return false;
            return IsProcessBlacklisted(processId);
        }

        internal bool IsProcessBlacklisted(uint processId)
        {
            return GetProcessBlacklistMode(processId).HasValue;
        }

        internal BlacklistMode? GetProcessBlacklistMode(uint processId)
        {
            long now = Environment.TickCount64;
            if (processId == _blacklistCachePid && now - _blacklistCacheTick < BlacklistCacheDurationMs)
            {
                return _blacklistCacheMode;
            }

            bool result = false;
            BlacklistMode? mode = null;
            try
            {
                var process = Process.GetProcessById((int)processId);
                string processName = process.ProcessName + ".exe";
                string? processPath = null;
                try
                {
                    processPath = process.MainModule?.FileName;
                }
                catch
                {
                    // 权限不足或进程已退出：退化为仅按进程名匹配
                }

                BlacklistEntry[] entries;
                try
                {
                    entries = System.Linq.Enumerable.ToArray(_settings.BlacklistProcesses);
                }
                catch
                {
                    // 列表可能正被 UI 线程修改，退化为本次不匹配
                    entries = Array.Empty<BlacklistEntry>();
                }

                BlacklistEntry? match = BlacklistMatcher.FindMatch(processName, processPath, entries);
                result = match != null;
                mode = match?.Mode;
            }
            catch
            {
                result = false;
            }

            _blacklistCachePid = processId;
            _blacklistCacheMode = mode;
            _blacklistCacheTick = now;
            return mode;
        }

        private BlacklistMode? GetWindowBlacklistMode(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return null;
            Win32Api.GetWindowThreadProcessId(hwnd, out uint processId);
            return processId == 0 ? null : GetProcessBlacklistMode(processId);
        }

        private bool IsCursorFunctionBlocked()
        {
            IntPtr target = Win32Api.GetRootWindowFromCursor();
            if (target == IntPtr.Zero) target = Win32Api.GetForegroundWindow();
            return GetWindowBlacklistMode(target).HasValue;
        }

        private bool IsForegroundFunctionBlocked()
        {
            return GetWindowBlacklistMode(Win32Api.GetForegroundWindow()).HasValue;
        }

        private void GlobalHook_MouseWheelExt(object? sender, MouseEventExtArgs e)
        {
            if (_settings.EnableTransparencyHotkey && IsHotkeyMatch(_settings.TransparencyHotkey, null, true))
            {
                if (IsCursorFunctionBlocked()) return; // 黑名单进程：忽略，且不拦截滚轮
                int delta = e.Delta > 0 ? 10 : -10;
                OnWindowTransparencyAdjustRequested?.Invoke(this, delta);
                e.Handled = true;
            }
            else if (_settings.EnableHeightHotkey && IsHotkeyMatch(_settings.HeightHotkey, null, true))
            {
                if (IsCursorFunctionBlocked()) return; // 黑名单进程：忽略，且不拦截滚轮
                int delta = e.Delta > 0 ? 30 : -30;
                OnWindowHeightAdjustRequested?.Invoke(this, delta);
                e.Handled = true;
            }
            else if (_settings.EnableWidthHotkey && IsHotkeyMatch(_settings.WidthHotkey, null, true))
            {
                if (IsCursorFunctionBlocked()) return; // 黑名单进程：忽略，且不拦截滚轮
                int delta = e.Delta > 0 ? 30 : -30;
                OnWindowWidthAdjustRequested?.Invoke(this, delta);
                e.Handled = true;
            }
        }

        private void GlobalHook_KeyDown(object? sender, KeyEventArgs e)
        {
            if (_settings.EnableAltNumpad && IsKeyPressed(VK_MENU) && !IsKeyPressed(VK_CONTROL) && !IsKeyPressed(VK_SHIFT))
            {
                if (e.KeyCode >= Keys.NumPad1 && e.KeyCode <= Keys.NumPad9)
                {
                    if (IsForegroundFunctionBlocked()) return; // 功能黑名单：忽略，且不拦截按键

                    WindowAlignment alignment = (WindowAlignment)(e.KeyCode - Keys.NumPad0);
                    OnWindowAlignmentRequested?.Invoke(this, alignment);
                    e.Handled = true;
                }
            }
            else if (_settings.EnableCtrlNumpad && IsKeyPressed(VK_CONTROL) && !IsKeyPressed(VK_MENU) && !IsKeyPressed(VK_SHIFT))
            {
                if (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9)
                {
                    if (IsCursorFunctionBlocked()) return; // 黑名单进程：忽略，且不拦截按键

                    int level = e.KeyCode - Keys.NumPad0;
                    int transparency = level == 0 ? 100 : (100 - (level * 10));
                    OnWindowTransparencyRequested?.Invoke(this, transparency);
                    e.Handled = true;
                }
            }
            else if (_settings.EnablePositionArrowKeys && IsKeyPressed(VK_CONTROL) && IsKeyPressed(VK_MENU) && !IsKeyPressed(VK_SHIFT))
            {
                int dx = 0, dy = 0;
                switch (e.KeyCode)
                {
                    case Keys.Left: dx = -15; break;
                    case Keys.Up: dy = -15; break;
                    case Keys.Right: dx = 15; break;
                    case Keys.Down: dy = 15; break;
                }
                if (dx != 0 || dy != 0)
                {
                    if (IsCursorFunctionBlocked()) return; // 黑名单进程：忽略，且不拦截按键

                    OnWindowPositionMoveRequested?.Invoke(this, (dx, dy));
                    e.Handled = true;
                }
            }
        }

        private void GlobalHook_MouseDownExt(object? sender, MouseEventExtArgs e)
        {
            bool matched = IsHotkeyMatch(_settings.MenuHotkey, e.Button);
            Core.MenuPerf.Trace($"MOUSEDOWN button={e.Button} match={matched}");
            if (matched)
            {
                IntPtr target = Win32Api.GetRootWindowFromCursor();
                if (GetWindowBlacklistMode(target) == BlacklistMode.Full)
                {
                    return; // 黑名单进程：忽略，且不拦截右键
                }

                e.Handled = true;
                Debug.WriteLine("Custom Hotkey Detected!");
                OnContextMenuRequested?.Invoke(this, new HookEventArgs(e.X, e.Y));
            }
            else
            {
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

        /// <summary>
        /// 重装全局钩子。内存紧张时低层钩子回调可能超过系统超时（LowLevelHooksTimeout），
        /// 被 Windows 静默移除后热键永久失效；定期重装可自愈。
        /// </summary>
        public void Restart()
        {
            Stop();
            Start();
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
