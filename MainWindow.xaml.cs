using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using SmartWindowTool.Core;
using SmartWindowTool.Models;
using SmartWindowTool.Views;

namespace SmartWindowTool
{
    public partial class MainWindow : Window
    {
        private readonly HookService _hookService;
        private readonly FloatingMenuWindow _floatingMenu;
        private readonly ViewModels.MainViewModel _viewModel;
        private System.Windows.Forms.NotifyIcon? _notifyIcon;

        public MainWindow(AppSettings settings)
        {
            InitializeComponent();
            WindowStyle = WindowStyle.None;

            _viewModel = new ViewModels.MainViewModel(settings);
            this.DataContext = _viewModel;

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
                ApplyDwmTheme(SystemThemeService.IsDarkTheme);
            };

            SystemThemeService.ThemeChanged += OnSystemThemeChanged;

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
            const int WM_NCHITTEST = 0x0084;
            const int WM_ERASEBKGND = 0x0014;
            if (msg == WM_NCHITTEST && WindowState == WindowState.Normal &&
                Win32Api.GetWindowRect(hwnd, out Win32Api.RECT rect))
            {
                long packedPoint = lParam.ToInt64();
                int cursorX = unchecked((short)(packedPoint & 0xFFFF));
                int cursorY = unchecked((short)((packedPoint >> 16) & 0xFFFF));
                int resizeBorder = Math.Max(1,
                    (int)Math.Ceiling(6 * System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX));

                bool left = cursorX < rect.Left + resizeBorder;
                bool right = cursorX >= rect.Right - resizeBorder;
                bool top = cursorY < rect.Top + resizeBorder;
                bool bottom = cursorY >= rect.Bottom - resizeBorder;

                int hitTest = top && left ? 13 :
                    top && right ? 14 :
                    bottom && left ? 16 :
                    bottom && right ? 17 :
                    left ? 10 :
                    right ? 11 :
                    top ? 12 :
                    bottom ? 15 : 1;
                if (hitTest != 1)
                {
                    handled = true;
                    return new IntPtr(hitTest);
                }
            }

            if (msg == WM_ERASEBKGND)
            {
                handled = true;
                return (IntPtr)1;
            }
            return IntPtr.Zero;
        }

        private void OnSystemThemeChanged(bool isDarkTheme)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => ApplyDwmTheme(isDarkTheme)));
                return;
            }

            ApplyDwmTheme(isDarkTheme);
        }

        private void ApplyDwmTheme(bool isDarkTheme)
        {
            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            int useDarkMode = isDarkTheme ? 1 : 0;
            Win32Api.DwmSetWindowAttribute(
                hwnd,
                Win32Api.DWMWA_USE_IMMERSIVE_DARK_MODE,
                ref useDarkMode,
                sizeof(int));
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            if (RootGrid == null) return;

            double radius = WindowState == WindowState.Maximized ? 0 : 8;
            RootGrid.Clip = new System.Windows.Media.RectangleGeometry(
                new Rect(0, 0, ActualWidth, ActualHeight),
                radius,
                radius);
            WindowFrameBorder.CornerRadius = new CornerRadius(radius);
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

            contextMenu.SetResourceReference(FrameworkElement.StyleProperty, "SmartContextMenuStyle");
            if (Application.Current.TryFindResource("SmartMenuItemStyle") is Style menuItemStyle)
            {
                contextMenu.Resources[typeof(System.Windows.Controls.MenuItem)] = menuItemStyle;
            }
            if (Application.Current.TryFindResource("SmartMenuSeparatorStyle") is Style separatorStyle)
            {
                contextMenu.Resources[typeof(System.Windows.Controls.Separator)] = separatorStyle;
            }

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

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
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

        public void ShowFromSecondaryInstance()
        {
            ShowMainWindow();
        }

        private void ExitApp_Click(object? sender, RoutedEventArgs? e)
        {
            _isRealExit = true;
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
            Application.Current.Shutdown();
        }

        private async void AutoStart_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.CheckBox checkBox) return;

            ((App)Application.Current).InvalidateAutoStartRefresh();
            bool previousValue = _viewModel.Settings.AutoStart;
            bool requestedValue = checkBox.IsChecked == true;
            checkBox.IsEnabled = false;

            try
            {
                AutoStartResult result = await AutoStartService.ConfigureForCurrentUserAsync(
                    requestedValue,
                    _viewModel.Settings.RunAsAdmin);
                if (result.Succeeded)
                {
                    _viewModel.Settings.AutoStart = requestedValue;
                }
                else
                {
                    checkBox.IsChecked = previousValue;
                    MessageBox.Show(result.ErrorMessage, "自启动设置失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            finally
            {
                checkBox.IsEnabled = true;
            }
        }

        private void OnAnyMouseDown(object? sender, Gma.System.MouseKeyHook.MouseEventExtArgs e)
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

        private void OnWindowAlignmentRequested(object? sender, WindowAlignment alignment)
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

        private void OnWindowTransparencyRequested(object? sender, int transparencyPercentage)
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

                Win32Api.GetWindowThreadProcessId(target, out uint pid);
                if (pid == (uint)Process.GetCurrentProcess().Id)
                {
                    Window? foundWindow = Application.Current.Windows.Cast<Window>()
                        .FirstOrDefault(w => new System.Windows.Interop.WindowInteropHelper(w).Handle == target);

                    if (foundWindow != null)
                    {
                        foundWindow.Opacity = pct / 100.0;
                        return;
                    }
                }

                SetNativeWindowOpacity(target, pct);
            });
        }

        private void OnWindowTransparencyAdjustRequested(object? sender, int deltaPercentage)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IntPtr target = Win32Api.GetRootWindowFromCursor();
                if (target == IntPtr.Zero)
                {
                    target = Win32Api.GetForegroundWindow();
                }
                if (target == IntPtr.Zero) return;

                Win32Api.GetWindowThreadProcessId(target, out uint pid);
                if (pid == (uint)Process.GetCurrentProcess().Id)
                {
                    Window? foundWindow = Application.Current.Windows.Cast<Window>()
                        .FirstOrDefault(w => new System.Windows.Interop.WindowInteropHelper(w).Handle == target);

                    if (foundWindow != null)
                    {
                        int currPct = (int)Math.Round(foundWindow.Opacity * 100);
                        int newPct = currPct + deltaPercentage;
                        if (newPct < 10) newPct = 10;
                        if (newPct > 100) newPct = 100;
                        foundWindow.Opacity = newPct / 100.0;
                        return;
                    }
                }

                int adjustedPercentage = Math.Clamp(
                    GetNativeWindowOpacityPercentage(target) + deltaPercentage,
                    10,
                    100);
                SetNativeWindowOpacity(target, adjustedPercentage);
            });
        }

        private static int GetNativeWindowOpacityPercentage(IntPtr hwnd)
        {
            uint exStyle = Win32Api.GetWindowLong(hwnd, Win32Api.GWL_EXSTYLE);
            if ((exStyle & Win32Api.WS_EX_LAYERED) != 0 &&
                Win32Api.GetLayeredWindowAttributes(hwnd, out _, out byte alpha, out uint flags) &&
                (flags & Win32Api.LWA_ALPHA) != 0)
            {
                return (int)Math.Round(alpha / 255.0 * 100.0);
            }

            return 100;
        }

        private static void SetNativeWindowOpacity(
            IntPtr hwnd,
            int opacityPercentage)
        {
            int clampedPercentage = Math.Clamp(opacityPercentage, 10, 100);
            uint exStyle = Win32Api.GetWindowLong(hwnd, Win32Api.GWL_EXSTYLE);
            bool isLayered = (exStyle & Win32Api.WS_EX_LAYERED) != 0;

            if (!isLayered)
            {
                Win32Api.SetWindowLong(hwnd, Win32Api.GWL_EXSTYLE, exStyle | Win32Api.WS_EX_LAYERED);
            }

            byte alpha = (byte)Math.Round(clampedPercentage / 100.0 * 255);
            Win32Api.SetLayeredWindowAttributes(hwnd, 0, alpha, Win32Api.LWA_ALPHA);
            Win32Api.InvalidateRect(hwnd, IntPtr.Zero, false);
            Win32Api.UpdateWindow(hwnd);
        }

        private void OnWindowHeightAdjustRequested(object? sender, int delta)
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

        private void OnWindowWidthAdjustRequested(object? sender, int delta)
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

        private void OnWindowPositionMoveRequested(object? sender, (int DeltaX, int DeltaY) e)
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

        private void OnContextMenuRequested(object? sender, HookEventArgs e)
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

                    _floatingMenu.Hide();
                    _floatingMenu.UpdateLayout();

                    // Pre-position the hidden native window so Show cannot submit a frame at a
                    // stale or off-screen location.
                    var initialPosition = GetFloatingMenuPosition(menuHwnd, e.MouseX, e.MouseY);
                    Win32Api.SetWindowPos(menuHwnd, new IntPtr(-1),
                        initialPosition.Left, initialPosition.Top, 0, 0,
                        Win32Api.SWP_NOSIZE | Win32Api.SWP_NOACTIVATE);

                    _floatingMenu.Show();
                    _floatingMenu.UpdateLayout();
                    var finalPosition = GetFloatingMenuPosition(menuHwnd, e.MouseX, e.MouseY);
                    _floatingMenu.Topmost = true;
                    Win32Api.SetWindowPos(menuHwnd, new IntPtr(-1),
                        finalPosition.Left, finalPosition.Top, 0, 0,
                        Win32Api.SWP_NOSIZE | Win32Api.SWP_NOACTIVATE | Win32Api.SWP_SHOWWINDOW);
                    Win32Api.SetForegroundWindow(menuHwnd);
                }
                else
                {
                    Console.WriteLine($"Invalid HWND: {rootHwnd} or is menu itself");
                }
            });
        }

        private (int Left, int Top) GetFloatingMenuPosition(IntPtr menuHwnd, int mouseX, int mouseY)
        {
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(mouseX, mouseY));
            var effectiveArea = GetEffectiveArea(screen);
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(_floatingMenu);
            int menuWidthPixels = Math.Max(170,
                (int)Math.Ceiling(_floatingMenu.ActualWidth * dpi.DpiScaleX));
            int menuHeightPixels = Math.Max(350,
                (int)Math.Ceiling(_floatingMenu.ActualHeight * dpi.DpiScaleY));
            if (Win32Api.GetWindowRect(menuHwnd, out Win32Api.RECT menuRect))
            {
                int nativeWidth = menuRect.Right - menuRect.Left;
                int nativeHeight = menuRect.Bottom - menuRect.Top;
                if (nativeWidth > 1 && nativeHeight > 1)
                {
                    menuWidthPixels = nativeWidth;
                    menuHeightPixels = nativeHeight;
                }
            }

            return ClampFloatingMenuPosition(
                effectiveArea,
                mouseX,
                mouseY,
                menuWidthPixels,
                menuHeightPixels);
        }

        internal static (int Left, int Top) ClampFloatingMenuPosition(
            System.Drawing.Rectangle effectiveArea,
            int mouseX,
            int mouseY,
            int menuWidthPixels,
            int menuHeightPixels)
        {
            int finalLeft = Math.Max(effectiveArea.Left,
                Math.Min(mouseX, effectiveArea.Right - menuWidthPixels));
            int finalTop = Math.Max(effectiveArea.Top,
                Math.Min(mouseY, effectiveArea.Bottom - menuHeightPixels));

            return (finalLeft, finalTop);
        }

        protected override void OnClosed(EventArgs e)
        {
            SystemThemeService.ThemeChanged -= OnSystemThemeChanged;
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
            string? processName = BlacklistInput.Text?.Trim();
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

        private async void RunAsAdmin_Click(object sender, RoutedEventArgs e)
        {
            bool isAdmin = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            if (_viewModel.Settings.RunAsAdmin && !isAdmin)
            {
                string? executablePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    _viewModel.Settings.RunAsAdmin = false;
                    return;
                }

                var processInfo = new ProcessStartInfo(executablePath)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = $"{App.ReplaceInstanceArgument} {App.SyncAutoStartArgument}"
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
                if (_viewModel.Settings.AutoStart)
                {
                    AutoStartResult result = await AutoStartService.ConfigureForCurrentUserAsync(true, false);
                    if (!result.Succeeded)
                    {
                        _viewModel.Settings.RunAsAdmin = true;
                        MessageBox.Show(result.ErrorMessage, "管理员权限设置失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                string? executablePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    _viewModel.Settings.RunAsAdmin = true;
                    return;
                }

                var processInfo = new ProcessStartInfo("explorer.exe", $"\"{executablePath}\"")
                {
                    UseShellExecute = true
                };
                try
                {
                    ((App)Application.Current).PrepareForRestart();
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
                    ((App)Application.Current).CancelRestart();
                    _viewModel.Settings.RunAsAdmin = true;
                }
            }
        }
    }
}
