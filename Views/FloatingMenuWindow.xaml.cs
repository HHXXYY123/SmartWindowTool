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
        
        // State tracking
        private Dictionary<IntPtr, int> _originalHeights = new Dictionary<IntPtr, int>();
        private Dictionary<IntPtr, uint> _originalStyles = new Dictionary<IntPtr, uint>();

        public class ScreenItem
        {
            public string Name { get; set; }
            public int Index { get; set; }
        }

        public FloatingMenuWindow(ViewModels.MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            this.DataContext = _viewModel;
            
            this.IsVisibleChanged += (s, e) =>
            {
                if (!this.IsVisible)
                {
                    CustomSizePopup.IsOpen = false;
                    MonitorPopup.IsOpen = false;
                }
            };
            
            // Allow menu to reposition itself if it exceeds screen bounds after expanding sizes
            this.SizeChanged += (s, e) => 
            {
                if (this.IsVisible && e.HeightChanged)
                {
                    AdjustPositionToScreen();
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
            if (popup.IsOpen && popup.Child is FrameworkElement child)
            {
                try
                {
                    var point = child.PointToScreen(new Point(0, 0));
                    var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(child);
                    double width = child.ActualWidth * dpi.DpiScaleX;
                    double height = child.ActualHeight * dpi.DpiScaleY;
                    
                    if (x >= point.X - 5 && x <= point.X + width + 5 &&
                        y >= point.Y - 5 && y <= point.Y + height + 5)
                    {
                        return true;
                    }
                }
                catch { }
            }
            return false;
        }

        public void AdjustPositionToScreen()
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            if (Win32Api.GetWindowRect(helper.Handle, out Win32Api.RECT rect))
            {
                var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(rect.Left, rect.Top));
                
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
                double logicalScreenHeight = screen.WorkingArea.Height / dpi.DpiScaleY;
                double logicalScreenTop = screen.WorkingArea.Top / dpi.DpiScaleY;
                double logicalScreenWidth = screen.WorkingArea.Width / dpi.DpiScaleX;
                double logicalScreenLeft = screen.WorkingArea.Left / dpi.DpiScaleX;

                bool changed = false;
                double newTop = this.Top;
                double newLeft = this.Left;

                // Adjust bottom edge
                if (this.Top + this.ActualHeight > logicalScreenTop + logicalScreenHeight)
                {
                    newTop = logicalScreenTop + logicalScreenHeight - this.ActualHeight;
                    changed = true;
                }
                
                // Adjust right edge
                if (this.Left + this.ActualWidth > logicalScreenLeft + logicalScreenWidth)
                {
                    newLeft = logicalScreenLeft + logicalScreenWidth - this.ActualWidth;
                    changed = true;
                }

                // If after adjusting the bottom edge, the top edge goes off-screen (menu is taller than screen)
                // or if we shrink the menu (collapse expander) and there's room to move back to the original mouse position,
                // we should re-adjust to not fly away.
                // But to make it simple and prevent the "fly away" behavior when collapsing:
                // Let's just always ensure the top edge doesn't go above the screen
                if (newTop < logicalScreenTop)
                {
                    newTop = logicalScreenTop;
                    changed = true;
                }

                if (changed)
                {
                    this.Top = newTop;
                    this.Left = newLeft;
                }
            }
        }

        public void UpdateState()
        {
            if (TargetWindowHwnd != IntPtr.Zero)
            {
                uint exStyle = Win32Api.GetWindowLong(TargetWindowHwnd, Win32Api.GWL_EXSTYLE);
                bool isTopmost = (exStyle & Win32Api.WS_EX_TOPMOST) != 0;
                PinButton.Content = isTopmost ? "取消置顶" : "置顶窗口";
                
                uint style = Win32Api.GetWindowLong(TargetWindowHwnd, Win32Api.GWL_STYLE);
                bool hasBorder = (style & Win32Api.WS_CAPTION) != 0;
                BorderlessButton.Content = hasBorder ? "移除边框" : "恢复边框";
                
                bool isRolledUp = _originalHeights.ContainsKey(TargetWindowHwnd);
                RollUpButton.Content = isRolledUp ? "展开窗口" : "卷起窗口";
                
                // Populate Monitors
                var screens = System.Windows.Forms.Screen.AllScreens;
                var screenItems = new List<ScreenItem>();
                for (int i = 0; i < screens.Length; i++)
                {
                    string name = screens[i].Primary ? $"显示器 {i + 1} (主)" : $"显示器 {i + 1}";
                    screenItems.Add(new ScreenItem { Name = name, Index = i });
                }
                MonitorList.ItemsSource = screenItems;
                
                Console.WriteLine($"UpdateState: Target={TargetWindowHwnd}, IsTopmost={isTopmost}, ExStyle={exStyle:X}, HasBorder={hasBorder}");
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
            if (TargetWindowHwnd != IntPtr.Zero)
            {
                // Enlarge window by 10%
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
            }
            this.Hide();
        }

        private void PinWindow_Click(object sender, RoutedEventArgs e)
        {
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

        private void CustomSizeButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            CustomSizePopup.IsOpen = true;
        }

        private void CustomSizeButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // Give it a tiny delay to allow the mouse to enter the popup
            System.Threading.Tasks.Task.Delay(50).ContinueWith(_ =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (!CustomSizeButton.IsMouseOver && !CustomSizePopup.IsMouseOver)
                    {
                        CustomSizePopup.IsOpen = false;
                    }
                });
            });
        }

        private void CustomSizePopup_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!CustomSizeButton.IsMouseOver && !CustomSizePopup.IsMouseOver)
            {
                CustomSizePopup.IsOpen = false;
            }
        }

        private void CustomSize_Click(object sender, RoutedEventArgs e)
        {
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

                string info = $"窗口句柄 (Handle): {TargetWindowHwnd}\n" +
                              $"窗口标题 (Title): {titleBuilder}\n" +
                              $"窗口类名 (Class): {classBuilder}\n" +
                              $"进程 ID (PID): {processId}\n" +
                              $"进程名称 (Process Name): {processName}\n" +
                              $"进程路径 (Process Path): {processPath}";

                MessageBox.Show(info, "窗口信息", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            this.Hide();
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (TargetWindowHwnd != IntPtr.Zero)
            {
                Win32Api.GetWindowThreadProcessId(TargetWindowHwnd, out uint processId);
                try
                {
                    var process = Process.GetProcessById((int)processId);
                    string path = process.MainModule?.FileName;
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
            if (TargetWindowHwnd != IntPtr.Zero)
            {
                if (Win32Api.GetWindowRect(TargetWindowHwnd, out Win32Api.RECT rect))
                {
                    int width = rect.Right - rect.Left;
                    int height = rect.Bottom - rect.Top;
                    
                    // Use WinForms Screen to get the physical bounds of the monitor the window is currently on
                    var screen = System.Windows.Forms.Screen.FromHandle(TargetWindowHwnd);
                    
                    int x = screen.WorkingArea.Left + (screen.WorkingArea.Width - width) / 2;
                    int y = screen.WorkingArea.Top + (screen.WorkingArea.Height - height) / 2;

                    Win32Api.SetWindowPos(TargetWindowHwnd, IntPtr.Zero, x, y, 0, 0, 
                        Win32Api.SWP_NOSIZE | Win32Api.SWP_NOZORDER | Win32Api.SWP_SHOWWINDOW);
                }
            }
            this.Hide();
        }

        private void MonitorButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            MonitorPopup.IsOpen = true;
        }

        private void MonitorButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            System.Threading.Tasks.Task.Delay(50).ContinueWith(_ =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (!MonitorButton.IsMouseOver && !MonitorPopup.IsMouseOver)
                    {
                        MonitorPopup.IsOpen = false;
                    }
                });
            });
        }

        private void MonitorPopup_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!MonitorButton.IsMouseOver && !MonitorPopup.IsMouseOver)
            {
                MonitorPopup.IsOpen = false;
            }
        }

        private void MoveToMonitor_Click(object sender, RoutedEventArgs e)
        {
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
                            
                            double relX = (double)(rect.Left - currentScreen.WorkingArea.Left) / currentScreen.WorkingArea.Width;
                            double relY = (double)(rect.Top - currentScreen.WorkingArea.Top) / currentScreen.WorkingArea.Height;

                            int newX = nextScreen.WorkingArea.Left + (int)(relX * nextScreen.WorkingArea.Width);
                            int newY = nextScreen.WorkingArea.Top + (int)(relY * nextScreen.WorkingArea.Height);

                            if (newX + width > nextScreen.WorkingArea.Right) newX = nextScreen.WorkingArea.Right - width;
                            if (newY + height > nextScreen.WorkingArea.Bottom) newY = nextScreen.WorkingArea.Bottom - height;
                            if (newX < nextScreen.WorkingArea.Left) newX = nextScreen.WorkingArea.Left;
                            if (newY < nextScreen.WorkingArea.Top) newY = nextScreen.WorkingArea.Top;

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
            if (TargetWindowHwnd != IntPtr.Zero)
            {
                if (Win32Api.GetWindowRect(TargetWindowHwnd, out Win32Api.RECT rect))
                {
                    int width = rect.Right - rect.Left;
                    int height = rect.Bottom - rect.Top;
                    
                    var screen = System.Windows.Forms.Screen.FromHandle(TargetWindowHwnd);
                    
                    int distLeft = Math.Abs(rect.Left - screen.WorkingArea.Left);
                    int distRight = Math.Abs(rect.Right - screen.WorkingArea.Right);
                    int distTop = Math.Abs(rect.Top - screen.WorkingArea.Top);
                    int distBottom = Math.Abs(rect.Bottom - screen.WorkingArea.Bottom);

                    int minX = Math.Min(distLeft, distRight);
                    int minY = Math.Min(distTop, distBottom);

                    int newX = rect.Left;
                    int newY = rect.Top;

                    // Snap horizontally if closer to horizontal edge
                    if (minX < minY)
                    {
                        newX = distLeft < distRight ? screen.WorkingArea.Left : screen.WorkingArea.Right - width;
                    }
                    else
                    {
                        newY = distTop < distBottom ? screen.WorkingArea.Top : screen.WorkingArea.Bottom - height;
                    }

                    Win32Api.SetWindowPos(TargetWindowHwnd, IntPtr.Zero, newX, newY, 0, 0, 
                        Win32Api.SWP_NOSIZE | Win32Api.SWP_NOZORDER | Win32Api.SWP_SHOWWINDOW);
                }
            }
            this.Hide();
        }
        
        private void HideWindow_Click(object sender, RoutedEventArgs e)
        {
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
