using System;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;

namespace SmartWindowTool.Models
{
    public class AppSettings : INotifyPropertyChanged
    {
        private bool _requireCtrl = true;
        private bool _requireShift = false;
        private bool _requireAlt = false;
        private string _triggerButton = "Right"; // "Right", "Middle", "Left", "None"
        private bool _silentStart = false;
        private bool _autoStart = false;
        private bool _runAsAdmin = false;
        
        public ObservableCollection<WindowSizeItem> CustomWindowSizes { get; set; } = new ObservableCollection<WindowSizeItem>();
        public ObservableCollection<string> BlacklistProcesses { get; set; } = new ObservableCollection<string>();

        public bool RequireCtrl
        {
            get => _requireCtrl;
            set { _requireCtrl = value; OnPropertyChanged(); }
        }

        public bool RequireShift
        {
            get => _requireShift;
            set { _requireShift = value; OnPropertyChanged(); }
        }

        public bool RequireAlt
        {
            get => _requireAlt;
            set { _requireAlt = value; OnPropertyChanged(); }
        }

        public string TriggerButton
        {
            get => _triggerButton;
            set { _triggerButton = value; OnPropertyChanged(); }
        }

        public bool SilentStart
        {
            get => _silentStart;
            set { _silentStart = value; OnPropertyChanged(); }
        }

        public bool AutoStart
        {
            get => _autoStart;
            set 
            { 
                _autoStart = value; 
                UpdateAutoStartRegistry(value);
                OnPropertyChanged(); 
            }
        }

        public bool RunAsAdmin
        {
            get => _runAsAdmin;
            set { _runAsAdmin = value; OnPropertyChanged(); }
        }

        private void UpdateAutoStartRegistry(bool enable)
        {
            if (!_isLoaded) return;
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    string appName = "SmartWindowTool";
                    if (enable)
                    {
                        string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            key.SetValue(appName, $"\"{exePath}\"");
                        }
                    }
                    else
                    {
                        key.DeleteValue(appName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to update AutoStart registry: {ex.Message}");
            }
        }

        private bool _isLoaded = false;

        public AppSettings()
        {
            CustomWindowSizes.CollectionChanged += (s, e) => Save();
        }

        public void EnsureDefaultSizes()
        {
            var defaults = new[]
            {
                new WindowSizeItem("640x480", 640, 480),
                new WindowSizeItem("720x480", 720, 480),
                new WindowSizeItem("720x576", 720, 576),
                new WindowSizeItem("800x600", 800, 600),
                new WindowSizeItem("1024x768", 1024, 768),
                new WindowSizeItem("1152x864", 1152, 864),
                new WindowSizeItem("1280x720", 1280, 720),
                new WindowSizeItem("1280x768", 1280, 768),
                new WindowSizeItem("1280x800", 1280, 800),
                new WindowSizeItem("1280x960", 1280, 960),
                new WindowSizeItem("1280x1024", 1280, 1024),
                new WindowSizeItem("1360x768", 1360, 768),
                new WindowSizeItem("1400x1050", 1400, 1050),
                new WindowSizeItem("1440x900", 1440, 900),
                new WindowSizeItem("1600x900", 1600, 900),
                new WindowSizeItem("1680x1050", 1680, 1050),
                new WindowSizeItem("1920x1080", 1920, 1080),
                new WindowSizeItem("2560x1440", 2560, 1440)
            };

            foreach (var item in defaults)
            {
                bool exists = false;
                foreach (var existing in CustomWindowSizes)
                {
                    if (existing.Width == item.Width && existing.Height == item.Height)
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                {
                    CustomWindowSizes.Add(item);
                }
            }
        }

        public MouseButtons GetParsedMouseButton()
        {
            if (TriggerButton == "Middle") return MouseButtons.Middle;
            if (TriggerButton == "Left") return MouseButtons.Left;
            if (TriggerButton == "None") return MouseButtons.None;
            return MouseButtons.Right;
        }

        public static AppSettings Load()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SmartWindowTool");
            string path = Path.Combine(dir, "SmartWindowTool.json");
            string oldPath = Path.Combine(dir, "settings.json");
            
            // Migrate old settings file if exists
            if (File.Exists(oldPath) && !File.Exists(path))
            {
                try
                {
                    File.Move(oldPath, path);
                }
                catch { }
            }

            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        settings.CustomWindowSizes.CollectionChanged += (s, e) => settings.Save();
                        if (settings.BlacklistProcesses == null)
                        {
                            settings.BlacklistProcesses = new ObservableCollection<string>();
                        }
                        settings.BlacklistProcesses.CollectionChanged += (s, e) => settings.Save();
                        settings.EnsureDefaultSizes();
                        settings._isLoaded = true;
                        
                        // Sync registry on load
                        settings.UpdateAutoStartRegistry(settings.AutoStart);
                        
                        return settings;
                    }
                }
                catch { }
            }
            var newSettings = new AppSettings();
            newSettings.EnsureDefaultSizes();
            newSettings._isLoaded = true;
            return newSettings;
        }

        public void Save()
        {
            if (!_isLoaded) return;
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SmartWindowTool");
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string path = Path.Combine(dir, "SmartWindowTool.json");
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch { }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            Save();
        }
    }
}