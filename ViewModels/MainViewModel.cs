using System;
using System.Collections.ObjectModel;
using System.Text;
using SmartWindowTool.Core;
using SmartWindowTool.Models;

namespace SmartWindowTool.ViewModels
{
    public class MainViewModel : System.ComponentModel.INotifyPropertyChanged
    {
        public ObservableCollection<HiddenWindowInfo> HiddenWindows { get; } = new ObservableCollection<HiddenWindowInfo>();
        
        // Expose visibility property instead of using converter
        public System.Windows.Visibility HiddenWindowsVisibility => HiddenWindows.Count > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        public AppSettings Settings { get; }

        public MainViewModel()
        {
            Settings = AppSettings.Load();
            
            HiddenWindows.CollectionChanged += (s, e) =>
            {
                // Notify UI to update visibility
                OnPropertyChanged(nameof(HiddenWindowsVisibility));
            };
        }
        
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }

        public void AddHiddenWindow(IntPtr hwnd, bool isTray = false)
        {
            var titleBuilder = new StringBuilder(256);
            Win32Api.GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
            
            var classBuilder = new StringBuilder(256);
            Win32Api.GetClassName(hwnd, classBuilder, classBuilder.Capacity);

            var info = new HiddenWindowInfo
            {
                Hwnd = hwnd,
                Title = titleBuilder.ToString(),
                ClassName = classBuilder.ToString(),
                HiddenAt = DateTime.Now,
                IsClickThrough = false,
                IsTray = isTray
            };

            if (isTray)
            {
                // Try to extract the window's icon
                System.Drawing.Icon icon = null;
                try
                {
                    // Attempt 1: Get window icon via WM_GETICON
                    IntPtr hIcon = Win32Api.SendMessage(hwnd, Win32Api.WM_GETICON, Win32Api.ICON_SMALL2, 0);
                    if (hIcon == IntPtr.Zero) hIcon = Win32Api.SendMessage(hwnd, Win32Api.WM_GETICON, Win32Api.ICON_SMALL, 0);
                    if (hIcon == IntPtr.Zero) hIcon = Win32Api.SendMessage(hwnd, Win32Api.WM_GETICON, Win32Api.ICON_BIG, 0);
                    if (hIcon == IntPtr.Zero) hIcon = Win32Api.GetClassLongPtr(hwnd, Win32Api.GCLP_HICONSM);
                    if (hIcon == IntPtr.Zero) hIcon = Win32Api.GetClassLongPtr(hwnd, Win32Api.GCLP_HICON);

                    if (hIcon != IntPtr.Zero)
                    {
                        icon = System.Drawing.Icon.FromHandle(hIcon);
                    }
                    else
                    {
                        // Attempt 2: Extract from process executable
                        Win32Api.GetWindowThreadProcessId(hwnd, out uint pid);
                        var process = System.Diagnostics.Process.GetProcessById((int)pid);
                        icon = System.Drawing.Icon.ExtractAssociatedIcon(process.MainModule.FileName);
                    }
                }
                catch { }

                if (icon == null) icon = System.Drawing.SystemIcons.Application;

                var trayIcon = new System.Windows.Forms.NotifyIcon
                {
                    Icon = icon,
                    Text = string.IsNullOrWhiteSpace(info.Title) ? info.ClassName : (info.Title.Length > 63 ? info.Title.Substring(0, 60) + "..." : info.Title),
                    Visible = true
                };

                trayIcon.MouseClick += (s, e) =>
                {
                    if (e.Button == System.Windows.Forms.MouseButtons.Left || e.Button == System.Windows.Forms.MouseButtons.Right)
                    {
                        RestoreWindowFromViewModel(info);
                    }
                };

                info.AppTrayIcon = trayIcon;
            }

            // Run on UI thread
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                HiddenWindows.Add(info);
            });
        }

        public void RestoreWindowFromViewModel(HiddenWindowInfo info)
        {
            if (info.IsClickThrough)
            {
                uint exStyle = Win32Api.GetWindowLong(info.Hwnd, Win32Api.GWL_EXSTYLE);
                exStyle &= ~Win32Api.WS_EX_TRANSPARENT;
                Win32Api.SetWindowLong(info.Hwnd, Win32Api.GWL_EXSTYLE, exStyle);
            }
            
            if (info.IsTray || !info.IsClickThrough)
            {
                Win32Api.ShowWindow(info.Hwnd, Win32Api.SW_SHOW);
                Win32Api.SetWindowPos(info.Hwnd, IntPtr.Zero, 0, 0, 0, 0, 
                    Win32Api.SWP_NOMOVE | Win32Api.SWP_NOSIZE | Win32Api.SWP_NOZORDER | Win32Api.SWP_SHOWWINDOW);
                Win32Api.SetForegroundWindow(info.Hwnd);
            }
            
            RemoveHiddenWindow(info);
        }

        public void AddClickThroughWindow(IntPtr hwnd)
        {
            var titleBuilder = new StringBuilder(256);
            Win32Api.GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
            
            var classBuilder = new StringBuilder(256);
            Win32Api.GetClassName(hwnd, classBuilder, classBuilder.Capacity);

            var info = new HiddenWindowInfo
            {
                Hwnd = hwnd,
                Title = titleBuilder.ToString(),
                ClassName = classBuilder.ToString(),
                HiddenAt = DateTime.Now,
                IsClickThrough = true
            };

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                HiddenWindows.Add(info);
            });
        }

        public void RemoveHiddenWindow(HiddenWindowInfo info)
        {
            if (info.AppTrayIcon != null)
            {
                info.AppTrayIcon.Visible = false;
                info.AppTrayIcon.Dispose();
                info.AppTrayIcon = null;
            }

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                HiddenWindows.Remove(info);
            });
        }

        public void RemoveCustomSize(WindowSizeItem item)
        {
            if (item != null)
            {
                Settings.CustomWindowSizes.Remove(item);
            }
        }

        public void AddCustomSize(string title, int width, int height)
        {
            Settings.CustomWindowSizes.Add(new WindowSizeItem(title, width, height));
        }
    }
}