using System;
using System.Diagnostics;
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

        public MainWindow()
        {
            InitializeComponent();
            
            _viewModel = new ViewModels.MainViewModel();
            this.DataContext = _viewModel;
            
            _floatingMenu = new FloatingMenuWindow(_viewModel);
            
            _hookService = new HookService(_viewModel.Settings);
            _hookService.OnContextMenuRequested += OnContextMenuRequested;
            _hookService.OnWindowAlignmentRequested += OnWindowAlignmentRequested;
            _hookService.OnWindowTransparencyRequested += OnWindowTransparencyRequested;
            _hookService.OnWindowTransparencyAdjustRequested += OnWindowTransparencyAdjustRequested;
            _hookService.OnAnyMouseDown += OnAnyMouseDown;
            _hookService.Start();
            
            this.Closing += MainWindow_Closing;
            
            InitializeTrayIcon();
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
            
            var exitItem = new System.Windows.Controls.MenuItem { Header = "退出程序" };
            exitItem.Click += (s, e) => ExitApp_Click(null, null);
            
            if (_viewModel.HiddenWindows.Count > 0)
            {
                contextMenu.Items.Add(new System.Windows.Controls.Separator());
                var restoreTitle = new System.Windows.Controls.MenuItem { Header = "--- 恢复隐藏的窗口 ---", IsEnabled = false };
                contextMenu.Items.Add(restoreTitle);
                
                foreach (var hiddenWin in _viewModel.HiddenWindows)
                {
                    var item = new System.Windows.Controls.MenuItem { Header = hiddenWin.DisplayText };
                    item.Click += (s, e) => RestoreWindow(hiddenWin);
                    contextMenu.Items.Add(item);
                }
            }

            contextMenu.Items.Add(new System.Windows.Controls.Separator());
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
                            x = screen.WorkingArea.Left;
                            y = screen.WorkingArea.Top;
                            break;
                        case WindowAlignment.TopCenter:
                            x = screen.WorkingArea.Left + (screen.WorkingArea.Width - width) / 2;
                            y = screen.WorkingArea.Top;
                            break;
                        case WindowAlignment.TopRight:
                            x = screen.WorkingArea.Right - width;
                            y = screen.WorkingArea.Top;
                            break;
                        case WindowAlignment.MiddleLeft:
                            x = screen.WorkingArea.Left;
                            y = screen.WorkingArea.Top + (screen.WorkingArea.Height - height) / 2;
                            break;
                        case WindowAlignment.Center:
                            x = screen.WorkingArea.Left + (screen.WorkingArea.Width - width) / 2;
                            y = screen.WorkingArea.Top + (screen.WorkingArea.Height - height) / 2;
                            break;
                        case WindowAlignment.MiddleRight:
                            x = screen.WorkingArea.Right - width;
                            y = screen.WorkingArea.Top + (screen.WorkingArea.Height - height) / 2;
                            break;
                        case WindowAlignment.BottomLeft:
                            x = screen.WorkingArea.Left;
                            y = screen.WorkingArea.Bottom - height;
                            break;
                        case WindowAlignment.BottomCenter:
                            x = screen.WorkingArea.Left + (screen.WorkingArea.Width - width) / 2;
                            y = screen.WorkingArea.Bottom - height;
                            break;
                        case WindowAlignment.BottomRight:
                            x = screen.WorkingArea.Right - width;
                            y = screen.WorkingArea.Bottom - height;
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
                if (target == IntPtr.Zero) return;

                byte alpha = (byte)(transparencyPercentage / 100.0 * 255);
                
                uint exStyle = Win32Api.GetWindowLong(target, Win32Api.GWL_EXSTYLE);
                if ((exStyle & Win32Api.WS_EX_LAYERED) == 0)
                {
                    Win32Api.SetWindowLong(target, Win32Api.GWL_EXSTYLE, exStyle | Win32Api.WS_EX_LAYERED);
                }

                Win32Api.SetLayeredWindowAttributes(target, 0, alpha, Win32Api.LWA_ALPHA);
            });
        }

        private void OnWindowTransparencyAdjustRequested(object sender, int deltaPercentage)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IntPtr target = Win32Api.GetRootWindowFromCursor();
                if (target == IntPtr.Zero) return;

                uint exStyle = Win32Api.GetWindowLong(target, Win32Api.GWL_EXSTYLE);
                byte currentAlpha = 255;
                if ((exStyle & Win32Api.WS_EX_LAYERED) != 0)
                {
                    if (Win32Api.GetLayeredWindowAttributes(target, out uint _, out byte bAlpha, out uint _))
                    {
                        currentAlpha = bAlpha;
                    }
                }

                int currentPercentage = (int)Math.Round(currentAlpha / 255.0 * 100.0);
                int newPercentage = currentPercentage + deltaPercentage;
                
                if (newPercentage < 10) newPercentage = 10;
                if (newPercentage > 100) newPercentage = 100;

                byte newAlpha = (byte)(newPercentage / 100.0 * 255);
                
                if ((exStyle & Win32Api.WS_EX_LAYERED) == 0)
                {
                    Win32Api.SetWindowLong(target, Win32Api.GWL_EXSTYLE, exStyle | Win32Api.WS_EX_LAYERED);
                }

                Win32Api.SetLayeredWindowAttributes(target, 0, newAlpha, Win32Api.LWA_ALPHA);
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
                    
                    // Use WorkingArea instead of Bounds to account for taskbar
                    double logicalScreenWidth = screen.WorkingArea.Width / dpi.DpiScaleX;
                    double logicalScreenHeight = screen.WorkingArea.Height / dpi.DpiScaleY;
                    double logicalScreenLeft = screen.WorkingArea.Left / dpi.DpiScaleX;
                    double logicalScreenTop = screen.WorkingArea.Top / dpi.DpiScaleY;

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
                if (info.IsClickThrough)
                {
                    uint exStyle = Win32Api.GetWindowLong(info.Hwnd, Win32Api.GWL_EXSTYLE);
                    Win32Api.SetWindowLong(info.Hwnd, Win32Api.GWL_EXSTYLE, exStyle & ~Win32Api.WS_EX_TRANSPARENT);
                }
                else
                {
                    Win32Api.ShowWindow(info.Hwnd, Win32Api.SW_SHOW);
                    Win32Api.SetForegroundWindow(info.Hwnd);
                }
                _viewModel.RemoveHiddenWindow(info);
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
                Process.Start(processInfo);
                _isRealExit = true;
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                }
                Application.Current.Shutdown();
            }
        }
    }
}