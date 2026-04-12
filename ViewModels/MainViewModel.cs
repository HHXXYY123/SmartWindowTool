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

            // Run on UI thread
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                HiddenWindows.Add(info);
            });
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