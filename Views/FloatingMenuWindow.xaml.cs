using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows;
using SmartWindowTool.Core;

namespace SmartWindowTool.Views
{
    public partial class FloatingMenuWindow : Window
    {
        public IntPtr TargetWindowHwnd { get; set; }

        private ViewModels.MainViewModel _viewModel;

        public bool IsFunctionOnlyBlocked { get; private set; }

        // State tracking
        private Dictionary<IntPtr, int> _originalHeights = new Dictionary<IntPtr, int>();
        private Dictionary<IntPtr, uint> _originalStyles = new Dictionary<IntPtr, uint>();

        // --- 悬停弹窗管理（安全三角）---
        private const int HoverPollMs = 25;      // 悬停状态采样间隔
        private const int CloseGraceMs = 200;    // 离开安全区后的收起宽限
        private const int SwitchDelayMs = 250;   // 切换到另一个弹窗的悬停意图延迟

        /// <summary>单个悬停弹窗的跟踪状态（触发按钮、安全三角、退出点）。</summary>
        private sealed class PopupHoverState
        {
            public PopupHoverState(System.Windows.Controls.Primitives.Popup popup, FrameworkElement trigger)
            {
                Popup = popup;
                Trigger = trigger;
            }

            public System.Windows.Controls.Primitives.Popup Popup { get; }
            public FrameworkElement Trigger { get; }
            public bool WasInside;                       // 上一采样时鼠标在按钮/弹窗内
            public System.Drawing.Point LastInsidePoint; // 最近一次在区域内时的光标位置（物理像素）
            public Core.SafeTriangle? Triangle;          // 离开区域后启用的安全三角
            public long OutsideSinceTick;                // 离开安全区的起始时刻（0 = 尚未离开）
        }

        private readonly PopupHoverState _customSizeHover;
        private readonly PopupHoverState _monitorHover;
        private System.Windows.Threading.DispatcherTimer? _hoverTimer;
        private System.Windows.Threading.DispatcherTimer? _switchTimer;
        private PopupHoverState? _pendingSwitch;

        // --- 呼出性能：查询缓存、预热、保活 ---
        private uint _cacheProcessId;
        private long _blacklistCacheTick;
        private Models.BlacklistEntry? _cachedBlacklistEntry;
        private List<ScreenItem>? _cachedScreenItems;
        private long _screensCacheTick;

        public class ScreenItem
        {
            public string Name { get; set; } = string.Empty;
            public int Index { get; set; }
        }

        public FloatingMenuWindow(ViewModels.MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            this.DataContext = _viewModel;

            _customSizeHover = new PopupHoverState(CustomSizePopup, CustomSizeButton);
            _monitorHover = new PopupHoverState(MonitorPopup, MonitorButton);

            this.IsVisibleChanged += (s, e) =>
            {
                if (!this.IsVisible)
                {
                    ClosePopup(_customSizeHover);
                    ClosePopup(_monitorHover);
                    CancelPendingSwitch();
                    StopHoverTimer();

                    // Clear focus to prevent the clicked button from showing a focus rectangle (white edge) on next show
                    System.Windows.Input.Keyboard.ClearFocus();
                    System.Windows.Input.FocusManager.SetFocusedElement(this, null);
                }
            };
        }

        public bool IsAnyPopupOpen()
        {
            return CustomSizePopup.IsOpen || MonitorPopup.IsOpen;
        }

        public bool IsMouseInsidePopup(int x, int y)
        {
            if (CheckPopup(CustomSizePopup, x, y)) return true;
            if (CheckPopup(MonitorPopup, x, y)) return true;
            return false;
        }

        private bool CheckPopup(System.Windows.Controls.Primitives.Popup popup, int x, int y)
        {
            if (popup.IsOpen && popup.Child is FrameworkElement)
            {
                var rect = GetPopupScreenRect(popup, 5);
                if (rect.Width > 0 && rect.Contains(x, y))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>取元素的物理屏幕矩形（可外扩 inflate 像素，用于容差命中）。</summary>
        private static System.Drawing.Rectangle GetElementScreenRect(FrameworkElement element, double inflate)
        {
            try
            {
                var origin = element.PointToScreen(new Point(0, 0));
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(element);
                double width = element.ActualWidth * dpi.DpiScaleX;
                double height = element.ActualHeight * dpi.DpiScaleY;
                return new System.Drawing.Rectangle(
                    (int)Math.Floor(origin.X - inflate),
                    (int)Math.Floor(origin.Y - inflate),
                    (int)Math.Ceiling(width + inflate * 2),
                    (int)Math.Ceiling(height + inflate * 2));
            }
            catch
            {
                return System.Drawing.Rectangle.Empty;
            }
        }

        /// <summary>取弹窗内容的物理屏幕矩形（可外扩 inflate 像素）。</summary>
        private static System.Drawing.Rectangle GetPopupScreenRect(System.Windows.Controls.Primitives.Popup popup, double inflate)
        {
            return popup.Child is FrameworkElement child
                ? GetElementScreenRect(child, inflate)
                : System.Drawing.Rectangle.Empty;
        }

        private System.Drawing.Rectangle GetEffectiveArea(System.Windows.Forms.Screen screen)
        {
            return _viewModel.Settings.IgnoreTaskbar ? screen.Bounds : screen.WorkingArea;
        }

        public void UpdateState()
        {
            if (TargetWindowHwnd != IntPtr.Zero)
            {
                Win32Api.GetWindowThreadProcessId(TargetWindowHwnd, out uint processId);
                var settingsEntry = FindBlacklistEntryCached(processId);
                IsFunctionOnlyBlocked = settingsEntry?.Mode == Models.BlacklistMode.FunctionOnly;
                SetFunctionControlsEnabled(!IsFunctionOnlyBlocked);

                uint exStyle = Win32Api.GetWindowLong(TargetWindowHwnd, Win32Api.GWL_EXSTYLE);
                bool isTopmost = (exStyle & Win32Api.WS_EX_TOPMOST) != 0;
                PinButton.Content = isTopmost ? "取消置顶" : "置顶窗口";
                
                uint style = Win32Api.GetWindowLong(TargetWindowHwnd, Win32Api.GWL_STYLE);
                bool hasBorder = (style & Win32Api.WS_CAPTION) != 0;
                BorderlessButton.Content = hasBorder ? "移除边框" : "恢复边框";
                
                bool isRolledUp = _originalHeights.ContainsKey(TargetWindowHwnd);
                RollUpButton.Content = isRolledUp ? "展开窗口" : "卷起窗口";

                EnsureMonitorList();
            }
        }

        /// <summary>
        /// 显示器列表缓存（30 秒）：Screen.AllScreens 每次查询都有可观开销，
        /// 而显示器配置极少变化；悬停「移动到显示器」时强制刷新。
        /// </summary>
        private void EnsureMonitorList(bool force = false)
        {
            if (force)
            {
                _screensCacheTick = 0;
            }
            if (_cachedScreenItems == null || Environment.TickCount64 - _screensCacheTick > 30_000)
            {
                var screens = System.Windows.Forms.Screen.AllScreens;
                var screenItems = new List<ScreenItem>();
                for (int i = 0; i < screens.Length; i++)
                {
                    string name = screens[i].Primary ? $"显示器 {i + 1} (主)" : $"显示器 {i + 1}";
                    screenItems.Add(new ScreenItem { Name = name, Index = i });
                }
                _cachedScreenItems = screenItems;
                _screensCacheTick = Environment.TickCount64;
            }
            MonitorList.ItemsSource = _cachedScreenItems;
        }

        /// <summary>
        /// 带缓存的黑名单查询：MainModule 跨进程查询有 10~100ms 级开销，
        /// 不能每次呼出都查。同一进程缓存 30 秒。
        /// </summary>
        private Models.BlacklistEntry? FindBlacklistEntryCached(uint processId)
        {
            long now = Environment.TickCount64;
            if (processId == _cacheProcessId && now - _blacklistCacheTick < 30_000)
            {
                return _cachedBlacklistEntry;
            }
            _cachedBlacklistEntry = FindBlacklistEntry(processId);
            _cacheProcessId = processId;
            _blacklistCacheTick = now;
            return _cachedBlacklistEntry;
        }

        /// <summary>
        /// 预热：提前完成 JIT 与布局（含两个悬停弹窗的内容），保证首次呼出
        /// （开机后、久未使用后）不卡。主窗口在屏幕外显隐。
        /// 注意：预热里绝不能打开弹窗本身——弹窗是独立置顶窗口，位置由触发按钮
        /// 推导，主窗口在屏幕外时 WPF 会把弹窗放进可见屏幕左上角（0,0），
        /// 每次保活都会在用户屏幕上闪现二级菜单（v1.4.2 引入的缺陷）。
        /// 这里只做弹窗内容的测量/布局（模板、列表项、JIT），不创建弹窗窗口。
        /// </summary>
        public void WarmUp()
        {
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                helper.EnsureHandle();
                Win32Api.SetWindowPos(helper.Handle, IntPtr.Zero, -32000, -32000, 0, 0,
                    Win32Api.SWP_NOSIZE | Win32Api.SWP_NOZORDER | Win32Api.SWP_NOACTIVATE);
                if (TargetWindowHwnd == IntPtr.Zero)
                {
                    TargetWindowHwnd = Win32Api.GetForegroundWindow();
                }
                UpdateState();
                if (!this.IsVisible)
                {
                    this.Show();
                    this.Hide();
                }
                WarmPopupContent(CustomSizePopup);
                WarmPopupContent(MonitorPopup);
            }
            catch
            {
                // 预热失败不影响正常使用
            }
        }

        /// <summary>预热弹窗内容（模板、列表项容器、布局/JIT），不打开弹窗窗口，不产生任何可见界面。</summary>
        private static void WarmPopupContent(System.Windows.Controls.Primitives.Popup popup)
        {
            if (popup.Child is not FrameworkElement child) return;
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            child.Arrange(new Rect(child.DesiredSize));
            child.UpdateLayout();
        }

        /// <summary>保活：菜单未显示时定期预热一次，保持关键页面驻留内存。</summary>
        public void KeepWarm()
        {
            if (this.IsVisible) return;
            WarmUp();
        }

        private Models.BlacklistEntry? FindBlacklistEntry(uint processId)
        {
            try
            {
                var process = Process.GetProcessById((int)processId);
                string processName = process.ProcessName + ".exe";
                string? processPath = null;
                try { processPath = process.MainModule?.FileName; } catch { }
                return Models.BlacklistMatcher.FindMatch(processName, processPath, _viewModel.Settings.BlacklistProcesses);
            }
            catch
            {
                return null;
            }
        }

        private void SetFunctionControlsEnabled(bool enabled)
        {
            SetControlTreeEnabled(FunctionControlsPanel, enabled);
            CustomSizePopup.IsEnabled = enabled;
            MonitorPopup.IsEnabled = enabled;
            CloseMenuButton.IsEnabled = true;
            FunctionBlockedHint.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        }

        private void SetControlTreeEnabled(DependencyObject root, bool enabled)
        {
            int childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is System.Windows.Controls.Control control && !ReferenceEquals(control, CloseMenuButton))
                {
                    control.IsEnabled = enabled;
                }
                SetControlTreeEnabled(child, enabled);
            }
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            // Now fully handled by HookService's OnAnyMouseDown to be 100% reliable across all applications
        }

        private void CloseMenu_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
        }

        private void EnlargeWindow_Click(object sender, RoutedEventArgs e)
        {
            if (IsFunctionOnlyBlocked)
            {
                this.Hide();
                return;
            }

            if (TargetWindowHwnd != IntPtr.Zero)
                if (Win32Api.GetWindowRect(TargetWindowHwnd, out Win32Api.RECT rect))
                {
                    int width = rect.Right - rect.Left;
                    int height = rect.Bottom - rect.Top;
                    int newWidth = (int)(width * 1.1);
                    int newHeight = (int)(height * 1.1);
                    
                    Win32Api.SetWindowPos(TargetWindowHwnd, IntPtr.Zero, 
                        rect.Left - (newWidth - width) / 2, 
                        rect.Top - (newHeight - height) / 2, 
                        newWidth, newHeight, 
                        Win32Api.SWP_NOZORDER | Win32Api.SWP_SHOWWINDOW);
                }
            this.Hide();
        }

        private void PinWindow_Click(object sender, RoutedEventArgs e)
        {
            if (IsFunctionOnlyBlocked) { Hide(); return; }
            if (TargetWindowHwnd != IntPtr.Zero)
            {
                uint exStyle = Win32Api.GetWindowLong(TargetWindowHwnd, Win32Api.GWL_EXSTYLE);
                bool isTopmost = (exStyle & Win32Api.WS_EX_TOPMOST) != 0;

                IntPtr hWndInsertAfter = isTopmost ? new IntPtr(-2) : new IntPtr(-1); // HWND_NOTOPMOST = -2, HWND_TOPMOST = -1

                bool result = Win32Api.SetWindowPos(TargetWindowHwnd, hWndInsertAfter, 0, 0, 0, 0, 
                    Win32Api.SWP_NOMOVE | Win32Api.SWP_NOSIZE | Win32Api.SWP_SHOWWINDOW);
                    
                Console.WriteLine($"PinWindow_Click: Target={TargetWindowHwnd}, WasTopmost={isTopmost}, SetResult={result}");
            }
            this.Hide();
        }

        // --- 悬停弹窗（安全三角）机制 ---

        private void CustomSizeButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            RequestOpenPopup(_customSizeHover);
        }

        private void MonitorButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            EnsureMonitorList(true); // 悬停时才强制刷新显示器列表，保证插拔显示器后立即准确
            RequestOpenPopup(_monitorHover);
        }

        /// <summary>请求打开弹窗：直接打开；若另一个弹窗开着则按悬停意图延迟切换，防止划过触发按钮把弹窗顶掉。</summary>
        private void RequestOpenPopup(PopupHoverState state)
        {
            CancelPendingSwitch();
            if (state.Popup.IsOpen) return;

            var other = ReferenceEquals(state, _customSizeHover) ? _monitorHover : _customSizeHover;
            if (other.Popup.IsOpen)
            {
                _pendingSwitch = state;
                if (_switchTimer == null)
                {
                    _switchTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(SwitchDelayMs)
                    };
                    _switchTimer.Tick += SwitchTimer_Tick;
                }
                _switchTimer.Stop();
                _switchTimer.Start();
                return;
            }
            OpenPopup(state);
        }

        private void SwitchTimer_Tick(object? sender, EventArgs e)
        {
            _switchTimer?.Stop();
            var state = _pendingSwitch;
            _pendingSwitch = null;
            if (state == null || !this.IsVisible) return;

            // 仍停留在触发按钮上才真正切换；已划走则维持原弹窗
            if (GetElementScreenRect(state.Trigger, 4).Contains(System.Windows.Forms.Cursor.Position))
            {
                OpenPopup(state);
            }
        }

        private void CancelPendingSwitch()
        {
            _pendingSwitch = null;
            _switchTimer?.Stop();
        }

        private void OpenPopup(PopupHoverState state)
        {
            var other = ReferenceEquals(state, _customSizeHover) ? _monitorHover : _customSizeHover;
            ClosePopup(other);
            state.WasInside = true;
            state.LastInsidePoint = System.Windows.Forms.Cursor.Position;
            state.Triangle = null;
            state.OutsideSinceTick = 0;
            state.Popup.IsOpen = true;
            EnsureHoverTimer();
        }

        private void ClosePopup(PopupHoverState state)
        {
            if (state.Popup.IsOpen)
            {
                state.Popup.IsOpen = false;
            }
            state.WasInside = false;
            state.Triangle = null;
            state.OutsideSinceTick = 0;
        }

        private void EnsureHoverTimer()
        {
            if (_hoverTimer == null)
            {
                _hoverTimer = new System.Windows.Threading.DispatcherTimer(
                    System.Windows.Threading.DispatcherPriority.Input)
                {
                    Interval = TimeSpan.FromMilliseconds(HoverPollMs)
                };
                _hoverTimer.Tick += HoverTimer_Tick;
            }
            if (!_hoverTimer.IsEnabled)
            {
                _hoverTimer.Start();
            }
        }

        private void StopHoverTimer()
        {
            _hoverTimer?.Stop();
        }

        private void HoverTimer_Tick(object? sender, EventArgs e)
        {
            var cursor = System.Windows.Forms.Cursor.Position;
            foreach (var state in new[] { _customSizeHover, _monitorHover })
            {
                if (!state.Popup.IsOpen) continue;
                EvaluateHover(state, cursor);
            }
            if (!_customSizeHover.Popup.IsOpen && !_monitorHover.Popup.IsOpen)
            {
                StopHoverTimer();
            }
        }

        /// <summary>
        /// 单个弹窗的悬停判定：
        /// 在按钮/弹窗内 → 保持并记录最后位置；
        /// 刚离开 → 用退出点对弹窗近边建立安全三角；
        /// 在三角内 → 保持；三角外超过宽限时间 → 收起。
        /// </summary>
        private void EvaluateHover(PopupHoverState state, System.Drawing.Point cursor)
        {
            var popupRect = GetPopupScreenRect(state.Popup, 0);
            if (popupRect.Width <= 0 || popupRect.Height <= 0)
            {
                // 弹窗尚未布局完成，保持打开
                state.WasInside = true;
                state.LastInsidePoint = cursor;
                state.Triangle = null;
                return;
            }

            var triggerRect = GetElementScreenRect(state.Trigger, 0);
            var triggerHitRect = GetElementScreenRect(state.Trigger, 4);
            var popupHitRect = GetPopupScreenRect(state.Popup, 5);
            if (triggerHitRect.Contains(cursor) || popupHitRect.Contains(cursor))
            {
                state.WasInside = true;
                state.LastInsidePoint = cursor;
                state.Triangle = null;
                state.OutsideSinceTick = 0;
                return;
            }

            if (state.WasInside)
            {
                // 刚离开按钮/弹窗：以最后在区域内的位置为顶点建立安全三角
                state.WasInside = false;
                state.Triangle = Core.SafeTriangleBuilder.Build(state.LastInsidePoint, popupRect, triggerRect);
            }

            if (state.Triangle.HasValue && state.Triangle.Value.Contains(cursor))
            {
                state.OutsideSinceTick = 0;
                return;
            }

            long now = Environment.TickCount64;
            if (state.OutsideSinceTick == 0)
            {
                state.OutsideSinceTick = now;
                return;
            }
            if (now - state.OutsideSinceTick < CloseGraceMs)
            {
                return;
            }
            ClosePopup(state);
        }

        private void CustomSize_Click(object sender, RoutedEventArgs e)
        {
            if (IsFunctionOnlyBlocked) { Hide(); return; }
            CustomSizePopup.IsOpen = false;
            if (sender is FrameworkElement element && element.DataContext is Models.WindowSizeItem sizeItem)
            {
                if (TargetWindowHwnd != IntPtr.Zero)
                {
                    if (Win32Api.GetWindowRect(TargetWindowHwnd, out Win32Api.RECT rect))
                    {
                        Win32Api.SetWindowPos(TargetWindowHwnd, IntPtr.Zero, 
                            rect.Left, 
                            rect.Top, 
                            sizeItem.Width, sizeItem.Height, 
                            Win32Api.SWP_NOZORDER | Win32Api.SWP_SHOWWINDOW);
                    }
                }
            }
            this.Hide();
        }

        private void ShrinkWindow_Click(object sender, RoutedEventArgs e)
        {
            if (IsFunctionOnlyBlocked) { Hide(); return; }
            if (TargetWindowHwnd != IntPtr.Zero)
            {
                // Shrink window by 10%
                if (Win32Api.GetWindowRect(TargetWindowHwnd, out Win32Api.RECT rect))
                {
                    int width = rect.Right - rect.Left;
                    int height = rect.Bottom - rect.Top;
                    int newWidth = (int)(width * 0.9);
                    int newHeight = (int)(height * 0.9);
                    
                    Win32Api.SetWindowPos(TargetWindowHwnd, IntPtr.Zero, 
                        rect.Left + (width - newWidth) / 2, 
                        rect.Top + (height - newHeight) / 2, 
                        newWidth, newHeight, 
                        Win32Api.SWP_NOZORDER | Win32Api.SWP_SHOWWINDOW);
                }
            }
            this.Hide();
        }

        private void TransparencySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (IsFunctionOnlyBlocked) return;
            if (TargetWindowHwnd != IntPtr.Zero && this.IsLoaded)
            {
                byte alpha = (byte)(e.NewValue / 100.0 * 255);
                
                // Ensure window has WS_EX_LAYERED style
                uint exStyle = Win32Api.GetWindowLong(TargetWindowHwnd, Win32Api.GWL_EXSTYLE);
                if ((exStyle & Win32Api.WS_EX_LAYERED) == 0)
                {
                    Win32Api.SetWindowLong(TargetWindowHwnd, Win32Api.GWL_EXSTYLE, exStyle | Win32Api.WS_EX_LAYERED);
                }

                Win32Api.SetLayeredWindowAttributes(TargetWindowHwnd, 0, alpha, Win32Api.LWA_ALPHA);
            }
        }

        private void InfoWindow_Click(object sender, RoutedEventArgs e)
        {
            if (IsFunctionOnlyBlocked) { Hide(); return; }
            if (TargetWindowHwnd != IntPtr.Zero)
            {
                var titleBuilder = new StringBuilder(256);
                Win32Api.GetWindowText(TargetWindowHwnd, titleBuilder, titleBuilder.Capacity);
                
                var classBuilder = new StringBuilder(256);
                Win32Api.GetClassName(TargetWindowHwnd, classBuilder, classBuilder.Capacity);

                Win32Api.GetWindowThreadProcessId(TargetWindowHwnd, out uint processId);
                
                string processName = "Unknown";
                string processPath = "Unknown";
                try
                {
                    var process = Process.GetProcessById((int)processId);
                    processName = process.ProcessName;
                    processPath = process.MainModule?.FileName ?? "Access Denied";
                }
                catch { }

                string handleStr = TargetWindowHwnd.ToString();
                string titleStr = titleBuilder.ToString();
                string classStr = classBuilder.ToString();
                string pidStr = processId.ToString();

                var infoWindow = new InfoWindow(handleStr, titleStr, classStr, pidStr, processName, processPath);
                this.Hide();
                infoWindow.Show();
                infoWindow.Activate();
                infoWindow.Focus();
                return;
            }
            this.Hide();
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (IsFunctionOnlyBlocked) { Hide(); return; }
            if (TargetWindowHwnd != IntPtr.Zero)
            {
                Win32Api.GetWindowThreadProcessId(TargetWindowHwnd, out uint processId);
                try
                {
                    var process = Process.GetProcessById((int)processId);
                    string? path = process.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(path))
                    {
                        Process.Start("explorer.exe", $"/select,\"{path}\"");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            this.Hide();
        }
        private void BottomWindow_Click(object sender, RoutedEventArgs e)
        {
            if (IsFunctionOnlyBlocked) { Hide(); return; }
            IntPtr target = TargetWindowHwnd;
            this.Hide();
            
            if (target != IntPtr.Zero)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    IntPtr HWND_BOTTOM = new IntPtr(1);
                    Win32Api.SetWindowPos(target, HWND_BOTTOM, 0, 0, 0, 0, 
                        Win32Api.SWP_NOMOVE | Win32Api.SWP_NOSIZE);
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void Borderless_Click(object sender, RoutedEventArgs e)
        {
            if (IsFunctionOnlyBlocked) { Hide(); return; }
            if (TargetWindowHwnd != IntPtr.Zero)
            {
                uint style = Win32Api.GetWindowLong(TargetWindowHwnd, Win32Api.GWL_STYLE);
                bool hasBorder = (style & Win32Api.WS_CAPTION) != 0;

                if (hasBorder)
                {
                    _originalStyles[TargetWindowHwnd] = style;
                    uint newStyle = style & ~Win32Api.WS_CAPTION & ~Win32Api.WS_THICKFRAME;
                    Win32Api.SetWindowLong(TargetWindowHwnd, Win32Api.GWL_STYLE, newStyle);
                }
                else
                {
                    if (_originalStyles.TryGetValue(TargetWindowHwnd, out uint originalStyle))
                    {
                        Win32Api.SetWindowLong(TargetWindowHwnd, Win32Api.GWL_STYLE, originalStyle);
                        _originalStyles.Remove(TargetWindowHwnd);
                    }
                }
                Win32Api.SetWindowPos(TargetWindowHwnd, IntPtr.Zero, 0, 0, 0, 0, 
                    Win32Api.SWP_NOMOVE | Win32Api.SWP_NOSIZE | Win32Api.SWP_NOZORDER | Win32Api.SWP_FRAMECHANGED);
            }
            this.Hide();
        }

        private void FreezeWindow_Click(object sender, RoutedEventArgs e)
        {
            if (IsFunctionOnlyBlocked) { Hide(); return; }
            if (TargetWindowHwnd != IntPtr.Zero)
            {
                if (Win32Api.GetWindowRect(TargetWindowHwnd, out Win32Api.RECT rect))
                {
                    int width = rect.Right - rect.Left;
                    int height = rect.Bottom - rect.Top;

                    IntPtr hdcWindow = Win32Api.GetWindowDC(TargetWindowHwnd);
                    IntPtr hdcMemDC = Win32Api.CreateCompatibleDC(hdcWindow);
                    IntPtr hBitmap = Win32Api.CreateCompatibleBitmap(hdcWindow, width, height);

                    if (hBitmap != IntPtr.Zero)
                    {
                        IntPtr hOld = Win32Api.SelectObject(hdcMemDC, hBitmap);
                        Win32Api.PrintWindow(TargetWindowHwnd, hdcMemDC, 2); // 2 is PW_RENDERFULLCONTENT
                        Win32Api.SelectObject(hdcMemDC, hOld);

                        var bmp = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmap, IntPtr.Zero, Int32Rect.Empty,
                            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

                        var freezeWindow = new FrozenImageWindow(bmp, rect.Left, rect.Top, width, height);
                        freezeWindow.Show();
                        
                        Win32Api.DeleteObject(hBitmap);
                    }
                    
                    Win32Api.DeleteDC(hdcMemDC);
                    Win32Api.ReleaseDC(TargetWindowHwnd, hdcWindow);
                }
            }
            this.Hide();
        }

        private void ClickThrough_Click(object sender, RoutedEventArgs e)
        {
            if (IsFunctionOnlyBlocked) { Hide(); return; }
            if (TargetWindowHwnd != IntPtr.Zero)
            {
                uint exStyle = Win32Api.GetWindowLong(TargetWindowHwnd, Win32Api.GWL_EXSTYLE);
                Win32Api.SetWindowLong(TargetWindowHwnd, Win32Api.GWL_EXSTYLE, exStyle | Win32Api.WS_EX_LAYERED | Win32Api.WS_EX_TRANSPARENT);
                _viewModel.AddClickThroughWindow(TargetWindowHwnd);
            }
            this.Hide();
        }

        private void RollUp_Click(object sender, RoutedEventArgs e)
        {
            if (IsFunctionOnlyBlocked) { Hide(); return; }
            if (TargetWindowHwnd != IntPtr.Zero)
            {
                if (_originalHeights.TryGetValue(TargetWindowHwnd, out int originalHeight))
                {
                    // Unroll
                    if (Win32Api.GetWindowRect(TargetWindowHwnd, out Win32Api.RECT rect))
                    {
                        int width = rect.Right - rect.Left;
                        Win32Api.SetWindowPos(TargetWindowHwnd, IntPtr.Zero, 0, 0, width, originalHeight, 
                            Win32Api.SWP_NOMOVE | Win32Api.SWP_NOZORDER);
                    }
                    _originalHeights.Remove(TargetWindowHwnd);
                }
                else
                {
                    // Roll up
                    if (Win32Api.GetWindowRect(TargetWindowHwnd, out Win32Api.RECT rect))
                    {
                        int width = rect.Right - rect.Left;
                        int height = rect.Bottom - rect.Top;
                        _originalHeights[TargetWindowHwnd] = height;
                        
                        // Roughly 30px for title bar height
                        int titleBarHeight = 30; 
                        Win32Api.SetWindowPos(TargetWindowHwnd, IntPtr.Zero, 0, 0, width, titleBarHeight, 
                            Win32Api.SWP_NOMOVE | Win32Api.SWP_NOZORDER);
                    }
                }
            }
            this.Hide();
        }
        private void CenterWindow_Click(object sender, RoutedEventArgs e)
        {
            if (IsFunctionOnlyBlocked) { Hide(); return; }
            if (TargetWindowHwnd != IntPtr.Zero)
            {
                if (Win32Api.GetWindowRect(TargetWindowHwnd, out Win32Api.RECT rect))
                {
                    int width = rect.Right - rect.Left;
                    int height = rect.Bottom - rect.Top;
                    
                    // Use WinForms Screen to get the physical bounds of the monitor the window is currently on
                    var screen = System.Windows.Forms.Screen.FromHandle(TargetWindowHwnd);
                    
                    int x = GetEffectiveArea(screen).Left + (GetEffectiveArea(screen).Width - width) / 2;
                    int y = GetEffectiveArea(screen).Top + (GetEffectiveArea(screen).Height - height) / 2;

                    Win32Api.SetWindowPos(TargetWindowHwnd, IntPtr.Zero, x, y, 0, 0, 
                        Win32Api.SWP_NOSIZE | Win32Api.SWP_NOZORDER | Win32Api.SWP_SHOWWINDOW);
                }
            }
            this.Hide();
        }

        private void MoveToMonitor_Click(object sender, RoutedEventArgs e)
        {
            if (IsFunctionOnlyBlocked) { Hide(); return; }
            MonitorPopup.IsOpen = false;
            if (sender is System.Windows.Controls.Button element && element.CommandParameter is int screenIndex)
            {
                if (TargetWindowHwnd != IntPtr.Zero)
                {
                    var screens = System.Windows.Forms.Screen.AllScreens;
                    if (screenIndex >= 0 && screenIndex < screens.Length)
                    {
                        var nextScreen = screens[screenIndex];
                        var currentScreen = System.Windows.Forms.Screen.FromHandle(TargetWindowHwnd);
                        
                        if (Win32Api.GetWindowRect(TargetWindowHwnd, out Win32Api.RECT rect))
                        {
                            int width = rect.Right - rect.Left;
                            int height = rect.Bottom - rect.Top;
                            
                            double relX = (double)(rect.Left - GetEffectiveArea(currentScreen).Left) / GetEffectiveArea(currentScreen).Width;
                            double relY = (double)(rect.Top - GetEffectiveArea(currentScreen).Top) / GetEffectiveArea(currentScreen).Height;

                            int newX = GetEffectiveArea(nextScreen).Left + (int)(relX * GetEffectiveArea(nextScreen).Width);
                            int newY = GetEffectiveArea(nextScreen).Top + (int)(relY * GetEffectiveArea(nextScreen).Height);

                            if (newX + width > GetEffectiveArea(nextScreen).Right) newX = GetEffectiveArea(nextScreen).Right - width;
                            if (newY + height > GetEffectiveArea(nextScreen).Bottom) newY = GetEffectiveArea(nextScreen).Bottom - height;
                            if (newX < GetEffectiveArea(nextScreen).Left) newX = GetEffectiveArea(nextScreen).Left;
                            if (newY < GetEffectiveArea(nextScreen).Top) newY = GetEffectiveArea(nextScreen).Top;

                            Win32Api.SetWindowPos(TargetWindowHwnd, IntPtr.Zero, newX, newY, width, height, 
                                Win32Api.SWP_NOZORDER | Win32Api.SWP_SHOWWINDOW);
                        }
                    }
                }
            }
            this.Hide();
        }

        private void SnapToEdge_Click(object sender, RoutedEventArgs e)
        {
            if (IsFunctionOnlyBlocked) { Hide(); return; }
            if (TargetWindowHwnd != IntPtr.Zero)
            {
                if (Win32Api.GetWindowRect(TargetWindowHwnd, out Win32Api.RECT rect))
                {
                    int width = rect.Right - rect.Left;
                    int height = rect.Bottom - rect.Top;
                    
                    var screen = System.Windows.Forms.Screen.FromHandle(TargetWindowHwnd);
                    
                    int distLeft = Math.Abs(rect.Left - GetEffectiveArea(screen).Left);
                    int distRight = Math.Abs(rect.Right - GetEffectiveArea(screen).Right);
                    int distTop = Math.Abs(rect.Top - GetEffectiveArea(screen).Top);
                    int distBottom = Math.Abs(rect.Bottom - GetEffectiveArea(screen).Bottom);

                    int minX = Math.Min(distLeft, distRight);
                    int minY = Math.Min(distTop, distBottom);

                    int newX = rect.Left;
                    int newY = rect.Top;

                    // Snap horizontally if closer to horizontal edge
                    if (minX < minY)
                    {
                        newX = distLeft < distRight ? GetEffectiveArea(screen).Left : GetEffectiveArea(screen).Right - width;
                    }
                    else
                    {
                        newY = distTop < distBottom ? GetEffectiveArea(screen).Top : GetEffectiveArea(screen).Bottom - height;
                    }

                    Win32Api.SetWindowPos(TargetWindowHwnd, IntPtr.Zero, newX, newY, 0, 0, 
                        Win32Api.SWP_NOSIZE | Win32Api.SWP_NOZORDER | Win32Api.SWP_SHOWWINDOW);
                }
            }
            this.Hide();
        }
        
        private void HideWindow_Click(object sender, RoutedEventArgs e)
        {
            if (IsFunctionOnlyBlocked) { Hide(); return; }
            if (TargetWindowHwnd != IntPtr.Zero)
            {
                Win32Api.SetWindowPos(TargetWindowHwnd, IntPtr.Zero, 0, 0, 0, 0, 
                    Win32Api.SWP_NOMOVE | Win32Api.SWP_NOSIZE | Win32Api.SWP_NOZORDER | Win32Api.SWP_HIDEWINDOW);
                _viewModel.AddHiddenWindow(TargetWindowHwnd);
            }
            this.Hide();
        }

        private void ShowMainWindow_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            foreach (Window window in Application.Current.Windows)
            {
                if (window is MainWindow mainWindow)
                {
                    mainWindow.Show();
                    mainWindow.WindowState = WindowState.Normal;
                    mainWindow.Activate();
                    break;
                }
            }
        }

        private void MinimizeToTray_Click(object sender, RoutedEventArgs e)
        {
            if (IsFunctionOnlyBlocked) { Hide(); return; }
            if (TargetWindowHwnd != IntPtr.Zero)
            {
                Win32Api.SetWindowPos(TargetWindowHwnd, IntPtr.Zero, 0, 0, 0, 0, 
                    Win32Api.SWP_NOMOVE | Win32Api.SWP_NOSIZE | Win32Api.SWP_NOZORDER | Win32Api.SWP_HIDEWINDOW);
                _viewModel.AddHiddenWindow(TargetWindowHwnd, true); // true = minimize to tray
            }
            this.Hide();
        }
    }
}
