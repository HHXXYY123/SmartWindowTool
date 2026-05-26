using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using SmartWindowTool.Core;
using SmartWindowTool.Models;
using SmartWindowTool.Views;

namespace SmartWindowTool
{
    public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
    {
        private HookService _hookService;
        private FloatingMenuWindow _floatingMenu;
        private ViewModels.MainViewModel _viewModel;
        private System.Windows.Forms.NotifyIcon _notifyIcon;
        private Wpf.Ui.Controls.WindowBackdropType _originalBackdropType;
        private bool _backdropOverridden;

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = new ViewModels.MainViewModel();
            this.DataContext = _viewModel;

            _originalBackdropType = this.WindowBackdropType;

            // Restore window position if saved
            if (!double.IsNaN(_viewModel.Settings.MainWindowLeft) && !double.IsNaN(_viewModel.Settings.MainWindowTop))
            {
                this.WindowStartupLocation = WindowStartupLocation.Manual;
                this.Left = _viewModel.Settings.MainWindowLeft;
                this.Top = _viewModel.Settings.MainWindowTop;
            }

            // 窗口初始化后设置 DWM 暗色模式和拦截白色背景擦除
            this.SourceInitialized += (s, e) =>
            {
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                var source = System.Windows.Interop.HwndSource.FromHwnd(hwnd);
                source.AddHook(WndProc);

                // 启用窗口暗色模式，使 DWM 框架（圆角区域等）使用深色而非白色
                int useDarkMode = 1;
                Win32Api.DwmSetWindowAttribute(hwnd, Win32Api.DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDarkMode, sizeof(int));
            };

            _floatingMenu = new FloatingMenuWindow(_viewModel);

            _hookService = new HookService(_viewModel.Settings);
            _hookService.OnContextMenuRequested += OnContextMenuRequested;
            _hookService.OnWindowAlignmentRequested += OnWindowAlignmentRequested;
            _hookService.OnWindowTransparencyRequested += OnWindowTransparencyRequested;
            _hookService.OnWindowTransparencyAdjustRequested += OnWindowTransparencyAdjustRequested;
            _hookService.OnWindowHeightAdjustRequested += OnWindowHeightAdjustRequested;
            _hookService.OnWindowWidthAdjustRequested += OnWindowWidthAdjustRequested;
            _hookService.OnAnyMouseDown += OnAnyMouseDown;
            _hookService.OnWindowPositionMoveRequested += OnWindowPositionMoveRequested;
            _hookService.Start();
            
            this.Closing += MainWindow_Closing;
            
            InitializeTrayIcon();
        }

        // 拦截系统擦除窗口背景的消息，防止 DWM 画白色底色
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_ERASEBKGND = 0x0014;
            if (msg == WM_ERASEBKGND)
            {
                handled = true;
                return (IntPtr)1;
            }
            return IntPtr.Zero;
        }

        private void InitializeTrayIcon()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            _notifyIcon.Text = "Smart Window Tool";
            _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            _notifyIcon.Visible = true;
            
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Left)
                {
                    ShowMainWindow();
                }
                else if (e.Button == System.Windows.Forms.MouseButtons.Right)
                {
                    Application.Current.Dispatcher.Invoke(() => ShowWpfTrayMenu());
                }
            };
            
            // Rebuild menu when HiddenWindows change
            _viewModel.HiddenWindows.CollectionChanged += (s, e) =>
            {
                // Nothing to do if we dynamically build the WPF menu each time
            };
        }

        private void ShowWpfTrayMenu()
        {
            var contextMenu = new System.Windows.Controls.ContextMenu();

            // Apply application resources to the ContextMenu so it inherits Wpf.Ui styles
            if (Application.Current.Resources.MergedDictionaries.Count > 0)
            {
                contextMenu.Resources.MergedDictionaries.Add(Application.Current.Resources.MergedDictionaries[0]);
            }
            contextMenu.SetResourceReference(FrameworkElement.StyleProperty, typeof(System.Windows.Controls.ContextMenu));

            var showItem = new System.Windows.Controls.MenuItem { Header = "显示主界面" };
            showItem.Click += (s, e) => ShowMainWindow();
            contextMenu.Items.Add(showItem);

            // ---- 恢复隐藏/最小化到托盘的窗口 ----
            if (_viewModel.HiddenWindows.Count > 0)
            {
                contextMenu.Items.Add(new System.Windows.Controls.Separator());
                var restoreMenu = new System.Windows.Controls.MenuItem { Header = $"恢复隐藏窗口 ({_viewModel.HiddenWindows.Count})" };

                foreach (var hiddenWin in _viewModel.HiddenWindows)
                {
                    var item = new System.Windows.Controls.MenuItem { Header = hiddenWin.DisplayText };
                    item.Click += (s, e) => _viewModel.RestoreWindowFromViewModel(hiddenWin);
                    restoreMenu.Items.Add(item);
                }
                contextMenu.Items.Add(restoreMenu);
            }

            // ---- 恢复鼠标穿透窗口 ----
            if (_viewModel.ClickThroughWindows.Count > 0)
            {
                contextMenu.Items.Add(new System.Windows.Controls.Separator());
                var ctMenu = new System.Windows.Controls.MenuItem { Header = $"恢复鼠标穿透窗口 ({_viewModel.ClickThroughWindows.Count})" };

                foreach (var ctWin in _viewModel.ClickThroughWindows)
                {
                    var item = new System.Windows.Controls.MenuItem { Header = ctWin.DisplayText };
                    item.Click += (s, e) => _viewModel.RestoreWindowFromViewModel(ctWin);
                    ctMenu.Items.Add(item);
                }
                contextMenu.Items.Add(ctMenu);
            }

            contextMenu.Items.Add(new System.Windows.Controls.Separator());
            var exitItem = new System.Windows.Controls.MenuItem { Header = "退出程序" };
            exitItem.Click += (s, e) => ExitApp_Click(null, null);
            contextMenu.Items.Add(exitItem);

            // Create a dummy hidden window to host the ContextMenu so it styles correctly and closes on click-away
            var hiddenWindow = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                ShowInTaskbar = false,
                Width = 0,
                Height = 0,
                Topmost = true
            };
            
            hiddenWindow.ContextMenu = contextMenu;
            contextMenu.Closed += (s, e) => hiddenWindow.Close();
            
            hiddenWindow.Show();
            
            // Move mouse to right bottomish to show menu near tray
            var cursor = System.Windows.Forms.Cursor.Position;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(hiddenWindow);
            hiddenWindow.Left = cursor.X / dpi.DpiScaleX;
            hiddenWindow.Top = cursor.Y / dpi.DpiScaleY;
            
            contextMenu.IsOpen = true;
            Win32Api.SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(hiddenWindow).Handle);
        }

        private void RestoreWindow(HiddenWindowInfo info)
        {
            if (info.IsClickThrough)
            {
                uint exStyle = Win32Api.GetWindowLong(info.Hwnd, Win32Api.GWL_EXSTYLE);
                exStyle &= ~Win32Api.WS_EX_TRANSPARENT;
                Win32Api.SetWindowLong(info.Hwnd, Win32Api.GWL_EXSTYLE, exStyle);
            }
            Win32Api.SetWindowPos(info.Hwnd, IntPtr.Zero, 0, 0, 0, 0, 
                Win32Api.SWP_NOMOVE | Win32Api.SWP_NOSIZE | Win32Api.SWP_NOZORDER | Win32Api.SWP_SHOWWINDOW);
            _viewModel.RemoveHiddenWindow(info);
        }

        private bool _isRealExit = false;

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isRealExit)
            {
                e.Cancel = true;
                this.Hide();
            }
            else
            {
                _viewModel.Settings.MainWindowLeft = this.Left;
                _viewModel.Settings.MainWindowTop = this.Top;
                _viewModel.Settings.Save();
            }
        }

        private void ShowMainWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        private void ExitApp_Click(object sender, RoutedEventArgs e)
        {
            _isRealExit = true;
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            this.Close();
        }

        private void OnAnyMouseDown(object sender, Gma.System.MouseKeyHook.MouseEventExtArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_floatingMenu.IsVisible)
                {
                    var helper = new System.Windows.Interop.WindowInteropHelper(_floatingMenu);
                    if (Win32Api.GetWindowRect(helper.Handle, out Win32Api.RECT rect))
                    {
                        // Check if the click is outside the bounds of the menu
                        // Note: Global hook gives physical pixels, Win32 GetWindowRect also gives physical pixels!
                        
                        // Ensure rect is valid (not 0,0,0,0) before dismissing
                        if (rect.Right > rect.Left && rect.Bottom > rect.Top)
                        {
                            // We add a tiny 5-pixel buffer to the bounds just to be safe
                            if (e.X < rect.Left - 5 || e.X > rect.Right + 5 || e.Y < rect.Top - 5 || e.Y > rect.Bottom + 5)
                            {
                                // Also check if popup is open and mouse is inside popup
                                if (_floatingMenu.IsAnyPopupOpen() && _floatingMenu.IsMouseInsidePopup(e.X, e.Y))
                                {
                                    return;
                                }
                                _floatingMenu.Hide();
                                Console.WriteLine($"Menu dismissed by global mouse hook. Clicked at ({e.X}, {e.Y}), Menu Bounds: ({rect.Left}, {rect.Top}, {rect.Right}, {rect.Bottom})");
                            }
                        }
                    }
                }
            });
        }

        private System.Drawing.Rectangle GetEffectiveArea(System.Windows.Forms.Screen screen)
        {
            return _viewModel.Settings.IgnoreTaskbar ? screen.Bounds : screen.WorkingArea;
        }

        private void OnWindowAlignmentRequested(object sender, WindowAlignment alignment)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IntPtr target = Win32Api.GetForegroundWindow();
                if (target == IntPtr.Zero) return;

                if (Win32Api.GetWindowRect(target, out Win32Api.RECT rect))
                {
                    int width = rect.Right - rect.Left;
                    int height = rect.Bottom - rect.Top;
                    
                    var screen = System.Windows.Forms.Screen.FromHandle(target);
                    int x = rect.Left;
                    int y = rect.Top;

                    switch (alignment)
                    {
                        case WindowAlignment.TopLeft:
                            x = GetEffectiveArea(screen).Left;
                            y = GetEffectiveArea(screen).Top;
                            break;
                        case WindowAlignment.TopCenter:
                            x = GetEffectiveArea(screen).Left + (GetEffectiveArea(screen).Width - width) / 2;
                            y = GetEffectiveArea(screen).Top;
                            break;
                        case WindowAlignment.TopRight:
                            x = GetEffectiveArea(screen).Right - width;
                            y = GetEffectiveArea(screen).Top;
                            break;
                        case WindowAlignment.MiddleLeft:
                            x = GetEffectiveArea(screen).Left;
                            y = GetEffectiveArea(screen).Top + (GetEffectiveArea(screen).Height - height) / 2;
                            break;
                        case WindowAlignment.Center:
                            x = GetEffectiveArea(screen).Left + (GetEffectiveArea(screen).Width - width) / 2;
                            y = GetEffectiveArea(screen).Top + (GetEffectiveArea(screen).Height - height) / 2;
                            break;
                        case WindowAlignment.MiddleRight:
                            x = GetEffectiveArea(screen).Right - width;
                            y = GetEffectiveArea(screen).Top + (GetEffectiveArea(screen).Height - height) / 2;
                            break;
                        case WindowAlignment.BottomLeft:
                            x = GetEffectiveArea(screen).Left;
                            y = GetEffectiveArea(screen).Bottom - height;
                            break;
                        case WindowAlignment.BottomCenter:
                            x = GetEffectiveArea(screen).Left + (GetEffectiveArea(screen).Width - width) / 2;
                            y = GetEffectiveArea(screen).Bottom - height;
                            break;
                        case WindowAlignment.BottomRight:
                            x = GetEffectiveArea(screen).Right - width;
                            y = GetEffectiveArea(screen).Bottom - height;
                            break;
                    }

                    Win32Api.SetWindowPos(target, IntPtr.Zero, x, y, 0, 0, 
                        Win32Api.SWP_NOSIZE | Win32Api.SWP_NOZORDER | Win32Api.SWP_SHOWWINDOW);
                }
            });
        }

        private void OnWindowTransparencyRequested(object sender, int transparencyPercentage)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IntPtr target = Win32Api.GetRootWindowFromCursor();
                if (target == IntPtr.Zero)
                {
                    target = Win32Api.GetForegroundWindow();
                }
                if (target == IntPtr.Zero) return;

                int pct = transparencyPercentage;
                if (pct < 10) pct = 10;
                if (pct > 100) pct = 100;

                // 检查是否是自己的窗口
                Win32Api.GetWindowThreadProcessId(target, out uint pid);
                if (pid == (uint)Process.GetCurrentProcess().Id)
                {
                    // 使用 WPF Window.Opacity（同步 Win32 层状态确保正确渲染）
                    Window foundWindow = Application.Current.Windows.Cast<Window>()
                        .FirstOrDefault(w => new System.Windows.Interop.WindowInteropHelper(w).Handle == target);

                    if (foundWindow != null)
                    {
                        foundWindow.Opacity = pct / 100.0;
                        // 同步设置 Win32 层叠窗口样式，保证 DWM 层面正确透明回写
                        IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(foundWindow).Handle;
                        uint exStyle = Win32Api.GetWindowLong(hwnd, Win32Api.GWL_EXSTYLE);
                        if ((exStyle & Win32Api.WS_EX_LAYERED) == 0)
                        {
                            Win32Api.SetWindowLong(hwnd, Win32Api.GWL_EXSTYLE, exStyle | Win32Api.WS_EX_LAYERED);
                            Win32Api.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                                Win32Api.SWP_NOMOVE | Win32Api.SWP_NOSIZE | Win32Api.SWP_NOZORDER | Win32Api.SWP_FRAMECHANGED);
                        }
                        byte alpha = (byte)(pct / 100.0 * 255);
                        Win32Api.SetLayeredWindowAttributes(hwnd, 0, alpha, Win32Api.LWA_ALPHA);
                        return;
                    }
                    else
                    {
                        // Win32 API 回退
                        byte alpha = (byte)(pct / 100.0 * 255);
                        uint exStyle = Win32Api.GetWindowLong(target, Win32Api.GWL_EXSTYLE);
                        if ((exStyle & Win32Api.WS_EX_LAYERED) == 0)
                        {
                            Win32Api.SetWindowLong(target, Win32Api.GWL_EXSTYLE, exStyle | Win32Api.WS_EX_LAYERED);
                            Win32Api.SetWindowPos(target, IntPtr.Zero, 0, 0, 0, 0,
                                Win32Api.SWP_NOMOVE | Win32Api.SWP_NOSIZE | Win32Api.SWP_NOZORDER | Win32Api.SWP_FRAMECHANGED);
                        }
                        Win32Api.SetLayeredWindowAttributes(target, 0, alpha, Win32Api.LWA_ALPHA);
                        Win32Api.InvalidateRect(target, IntPtr.Zero, false);
                        Win32Api.UpdateWindow(target);
                        int cornerPref = Win32Api.DWMWCP_ROUND;
                        Win32Api.DwmSetWindowAttribute(target, Win32Api.DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));
                        return;
                    }
                }

                // 其他程序的窗口，使用 Win32 API
                byte alpha2 = (byte)(pct / 100.0 * 255);
                uint exStyle2 = Win32Api.GetWindowLong(target, Win32Api.GWL_EXSTYLE);
                if ((exStyle2 & Win32Api.WS_EX_LAYERED) == 0)
                {
                    Win32Api.SetWindowLong(target, Win32Api.GWL_EXSTYLE, exStyle2 | Win32Api.WS_EX_LAYERED);
                }
                Win32Api.SetLayeredWindowAttributes(target, 0, alpha2, Win32Api.LWA_ALPHA);
                Win32Api.InvalidateRect(target, IntPtr.Zero, false);
                Win32Api.UpdateWindow(target);
            });
        }

        private void OnWindowTransparencyAdjustRequested(object sender, int deltaPercentage)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IntPtr target = Win32Api.GetRootWindowFromCursor();
                if (target == IntPtr.Zero)
                {
                    target = Win32Api.GetForegroundWindow();
                }
                if (target == IntPtr.Zero) return;

                // 检查是否是自己的窗口
                Win32Api.GetWindowThreadProcessId(target, out uint pid);
                if (pid == (uint)Process.GetCurrentProcess().Id)
                {
                    // 使用 WPF Window.Opacity（同步 Win32 层状态确保正确渲染）
                    Window foundWindow = Application.Current.Windows.Cast<Window>()
                        .FirstOrDefault(w => new System.Windows.Interop.WindowInteropHelper(w).Handle == target);

                    if (foundWindow != null)
                    {
                        int currPct = (int)Math.Round(foundWindow.Opacity * 100);
                        int newPct = currPct + deltaPercentage;
                        if (newPct < 10) newPct = 10;
                        if (newPct > 100) newPct = 100;
                        foundWindow.Opacity = newPct / 100.0;
                        // 同步设置 Win32 层叠窗口样式
                        IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(foundWindow).Handle;
                        uint exStyle = Win32Api.GetWindowLong(hwnd, Win32Api.GWL_EXSTYLE);
                        if ((exStyle & Win32Api.WS_EX_LAYERED) == 0)
                        {
                            Win32Api.SetWindowLong(hwnd, Win32Api.GWL_EXSTYLE, exStyle | Win32Api.WS_EX_LAYERED);
                            Win32Api.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                                Win32Api.SWP_NOMOVE | Win32Api.SWP_NOSIZE | Win32Api.SWP_NOZORDER | Win32Api.SWP_FRAMECHANGED);
                        }
                        byte alpha = (byte)(newPct / 100.0 * 255);
                        Win32Api.SetLayeredWindowAttributes(hwnd, 0, alpha, Win32Api.LWA_ALPHA);
                        return;
                    }
                    else
                    {
                        // Win32 API 回退
                        uint exStyle = Win32Api.GetWindowLong(target, Win32Api.GWL_EXSTYLE);
                        byte currentAlpha = 255;
                        if ((exStyle & Win32Api.WS_EX_LAYERED) != 0)
                        {
                            if (Win32Api.GetLayeredWindowAttributes(target, out uint _, out byte bAlpha, out uint _))
                            {
                                currentAlpha = bAlpha;
                            }
                        }

                        int targetPercentage = (int)Math.Round(currentAlpha / 255.0 * 100.0);
                        int adjustedPercentage = targetPercentage + deltaPercentage;
                        if (adjustedPercentage < 10) adjustedPercentage = 10;
                        if (adjustedPercentage > 100) adjustedPercentage = 100;
                        byte newAlpha = (byte)(adjustedPercentage / 100.0 * 255);

                        if ((exStyle & Win32Api.WS_EX_LAYERED) == 0)
                        {
                            Win32Api.SetWindowLong(target, Win32Api.GWL_EXSTYLE, exStyle | Win32Api.WS_EX_LAYERED);
                            Win32Api.SetWindowPos(target, IntPtr.Zero, 0, 0, 0, 0,
                                Win32Api.SWP_NOMOVE | Win32Api.SWP_NOSIZE | Win32Api.SWP_NOZORDER | Win32Api.SWP_FRAMECHANGED);
                        }
                        Win32Api.SetLayeredWindowAttributes(target, 0, newAlpha, Win32Api.LWA_ALPHA);
                        Win32Api.InvalidateRect(target, IntPtr.Zero, false);
                        Win32Api.UpdateWindow(target);
                        int cornerPref = Win32Api.DWMWCP_ROUND;
                        Win32Api.DwmSetWindowAttribute(target, Win32Api.DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));
                        return;
                    }
                }

                // 其他程序的窗口，使用 Win32 API
                uint exStyle2 = Win32Api.GetWindowLong(target, Win32Api.GWL_EXSTYLE);
                byte currentAlpha2 = 255;
                if ((exStyle2 & Win32Api.WS_EX_LAYERED) != 0)
                {
                    if (Win32Api.GetLayeredWindowAttributes(target, out uint _, out byte bAlpha, out uint _))
                    {
                        currentAlpha2 = bAlpha;
                    }
                }

                int targetPercentage2 = (int)Math.Round(currentAlpha2 / 255.0 * 100.0);
                int adjustedPercentage2 = targetPercentage2 + deltaPercentage;
                if (adjustedPercentage2 < 10) adjustedPercentage2 = 10;
                if (adjustedPercentage2 > 100) adjustedPercentage2 = 100;
                byte newAlpha2 = (byte)(adjustedPercentage2 / 100.0 * 255);

                if ((exStyle2 & Win32Api.WS_EX_LAYERED) == 0)
                {
                    Win32Api.SetWindowLong(target, Win32Api.GWL_EXSTYLE, exStyle2 | Win32Api.WS_EX_LAYERED);
                }
                Win32Api.SetLayeredWindowAttributes(target, 0, newAlpha2, Win32Api.LWA_ALPHA);
                Win32Api.InvalidateRect(target, IntPtr.Zero, false);
                Win32Api.UpdateWindow(target);
            });
        }

        private void OnWindowHeightAdjustRequested(object sender, int delta)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IntPtr target = Win32Api.GetRootWindowFromCursor();
                if (target == IntPtr.Zero) return;

                if (Win32Api.GetWindowRect(target, out Win32Api.RECT rect))
                {
                    int width = rect.Right - rect.Left;
                    int height = rect.Bottom - rect.Top + delta;
                    if (height < 100) height = 100; // Minimum height to prevent window from disappearing
                    
                    Win32Api.SetWindowPos(target, IntPtr.Zero, 0, 0, width, height, 
                        Win32Api.SWP_NOMOVE | Win32Api.SWP_NOZORDER | Win32Api.SWP_SHOWWINDOW);
                }
            });
        }

        private void OnWindowWidthAdjustRequested(object sender, int delta)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IntPtr target = Win32Api.GetRootWindowFromCursor();
                if (target == IntPtr.Zero) return;

                if (Win32Api.GetWindowRect(target, out Win32Api.RECT rect))
                {
                    int width = rect.Right - rect.Left + delta;
                    int height = rect.Bottom - rect.Top;
                    if (width < 100) width = 100; // Minimum width

                    Win32Api.SetWindowPos(target, IntPtr.Zero, 0, 0, width, height,
                        Win32Api.SWP_NOMOVE | Win32Api.SWP_NOZORDER | Win32Api.SWP_SHOWWINDOW);
                }
            });
        }

        private void OnWindowPositionMoveRequested(object sender, (int DeltaX, int DeltaY) e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IntPtr target = Win32Api.GetRootWindowFromCursor();
                if (target == IntPtr.Zero) return;

                if (Win32Api.GetWindowRect(target, out Win32Api.RECT rect))
                {
                    int newX = rect.Left + e.DeltaX;
                    int newY = rect.Top + e.DeltaY;

                    Win32Api.SetWindowPos(target, IntPtr.Zero, newX, newY, 0, 0,
                        Win32Api.SWP_NOSIZE | Win32Api.SWP_NOZORDER | Win32Api.SWP_SHOWWINDOW);
                }
            });
        }

        private void OnContextMenuRequested(object sender, HookEventArgs e)
        {
            // Run on UI thread
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Ensure the window handle is created
                var helper = new System.Windows.Interop.WindowInteropHelper(_floatingMenu);
                helper.EnsureHandle();
                IntPtr menuHwnd = helper.Handle;

                // Get the target window under the cursor
                IntPtr rootHwnd = Win32Api.GetRootWindowFromCursor();
                
                if (rootHwnd != IntPtr.Zero && rootHwnd != menuHwnd)
                {
                    _floatingMenu.TargetWindowHwnd = rootHwnd;
                    _floatingMenu.UpdateState();
                    
                    // Position the menu, considering DPI scaling
                    var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
                    double logicalMouseX = e.MouseX / dpi.DpiScaleX;
                    double logicalMouseY = e.MouseY / dpi.DpiScaleY;
                    
                    // We no longer rely on Deactivated event, so we can simplify the show logic
                    _floatingMenu.Hide();
                    
                    // Measure the menu's desired size without showing it
                    _floatingMenu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    double menuWidth = _floatingMenu.DesiredSize.Width;
                    double menuHeight = _floatingMenu.DesiredSize.Height;
                    
                    // Fallback to defaults if measure fails
                    if (menuWidth == 0) menuWidth = 170;
                    if (menuHeight == 0) menuHeight = 350;
                    
                    // Reposition menu if it goes out of screen bounds
                    var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(e.MouseX, e.MouseY));
                    
                    // Use effective area (taskbar-aware or full screen based on setting)
                    double logicalScreenWidth = GetEffectiveArea(screen).Width / dpi.DpiScaleX;
                    double logicalScreenHeight = GetEffectiveArea(screen).Height / dpi.DpiScaleY;
                    double logicalScreenLeft = GetEffectiveArea(screen).Left / dpi.DpiScaleX;
                    double logicalScreenTop = GetEffectiveArea(screen).Top / dpi.DpiScaleY;

                    double finalLeft = logicalMouseX;
                    double finalTop = logicalMouseY;

                    if (finalLeft + menuWidth > logicalScreenLeft + logicalScreenWidth)
                    {
                        finalLeft = logicalScreenLeft + logicalScreenWidth - menuWidth;
                    }
                    if (finalTop + menuHeight > logicalScreenTop + logicalScreenHeight)
                    {
                        finalTop = logicalScreenTop + logicalScreenHeight - menuHeight;
                    }
                    
                    // Position exactly before showing to prevent ANY flicker
                    _floatingMenu.Left = finalLeft;
                    _floatingMenu.Top = finalTop;
                    _floatingMenu.Opacity = 1;
                    _floatingMenu.Show();
                    
                    // Enforce Topmost and foreground to prevent the menu from hiding behind other windows
                    _floatingMenu.Topmost = true;
                    Win32Api.SetWindowPos(menuHwnd, new IntPtr(-1), 0, 0, 0, 0, Win32Api.SWP_NOMOVE | Win32Api.SWP_NOSIZE | Win32Api.SWP_SHOWWINDOW);
                    Win32Api.SetForegroundWindow(menuHwnd);
                }
                else
                {
                    Console.WriteLine($"Invalid HWND: {rootHwnd} or is menu itself");
                }
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            _hookService.Stop();
            _floatingMenu.Close();

            // Restore all hidden/click-through windows before exiting to prevent them from being lost
            foreach (var hw in _viewModel.HiddenWindows)
            {
                if (hw.IsClickThrough)
                {
                    uint exStyle = Win32Api.GetWindowLong(hw.Hwnd, Win32Api.GWL_EXSTYLE);
                    Win32Api.SetWindowLong(hw.Hwnd, Win32Api.GWL_EXSTYLE, exStyle & ~Win32Api.WS_EX_TRANSPARENT);
                }
                else
                {
                    Win32Api.ShowWindow(hw.Hwnd, Win32Api.SW_SHOW);
                }
            }

            // Also restore click-through windows that weren't in HiddenWindows
            foreach (var hw in _viewModel.ClickThroughWindows)
            {
                uint exStyle = Win32Api.GetWindowLong(hw.Hwnd, Win32Api.GWL_EXSTYLE);
                Win32Api.SetWindowLong(hw.Hwnd, Win32Api.GWL_EXSTYLE, exStyle & ~Win32Api.WS_EX_TRANSPARENT);
            }

            base.OnClosed(e);
        }

        private void RemoveSize_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button element && element.CommandParameter is Models.WindowSizeItem sizeItem)
            {
                _viewModel.RemoveCustomSize(sizeItem);
            }
        }

        private void AddBlacklist_Click(object sender, RoutedEventArgs e)
        {
            string processName = BlacklistInput.Text?.Trim();
            if (!string.IsNullOrEmpty(processName))
            {
                if (!processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    processName += ".exe";
                }
                
                if (!_viewModel.Settings.BlacklistProcesses.Contains(processName))
                {
                    _viewModel.Settings.BlacklistProcesses.Add(processName);
                }
                BlacklistInput.Text = string.Empty;
            }
        }

        private void SelectProcess_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "可执行文件 (*.exe)|*.exe",
                Title = "选择要屏蔽的程序"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string fileName = System.IO.Path.GetFileName(openFileDialog.FileName);
                BlacklistInput.Text = fileName;
            }
        }

        private void RemoveBlacklist_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button element && element.CommandParameter is string processName)
            {
                _viewModel.Settings.BlacklistProcesses.Remove(processName);
            }
        }

        private void AddSize_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(NewSizeWidth.Text, out int width) && int.TryParse(NewSizeHeight.Text, out int height))
            {
                string title = string.IsNullOrWhiteSpace(NewSizeTitle.Text) ? $"{width}x{height}" : NewSizeTitle.Text;
                _viewModel.AddCustomSize(title, width, height);
                
                NewSizeTitle.Text = "";
                NewSizeWidth.Text = "";
                NewSizeHeight.Text = "";
            }
            else
            {
                MessageBox.Show("请输入有效的宽度和高度数字。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        private void RestoreWindow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is Models.HiddenWindowInfo info)
            {
                _viewModel.RestoreWindowFromViewModel(info);
            }
        }

        private void RunAsAdmin_Click(object sender, RoutedEventArgs e)
        {
            bool isAdmin = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            if (_viewModel.Settings.RunAsAdmin && !isAdmin)
            {
                var processInfo = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };
                try
                {
                    Process.Start(processInfo);
                    _isRealExit = true;
                    if (_notifyIcon != null)
                    {
                        _notifyIcon.Visible = false;
                        _notifyIcon.Dispose();
                    }
                    Application.Current.Shutdown();
                }
                catch
                {
                    _viewModel.Settings.RunAsAdmin = false;
                }
            }
            else if (!_viewModel.Settings.RunAsAdmin && isAdmin)
            {
                var processInfo = new ProcessStartInfo("explorer.exe", Process.GetCurrentProcess().MainModule.FileName)
                {
                    UseShellExecute = true
                };
                try
                {
                    Process.Start(processInfo);
                    _isRealExit = true;
                    if (_notifyIcon != null)
                    {
                        _notifyIcon.Visible = false;
                        _notifyIcon.Dispose();
                    }
                    Application.Current.Shutdown();
                }
                catch
                {
                    _viewModel.Settings.RunAsAdmin = true;
                }
            }
        }
    }
}
