using System;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;

namespace SmartWindowTool.Models
{
    public class HotkeyConfig : INotifyPropertyChanged
    {
        private string _modifier1 = "Ctrl";
        private string _modifier2 = "Shift";
        private string _key = "None";
        private string _mouseButton = "Right";

        public string Modifier1 { get => _modifier1; set { _modifier1 = value; OnPropertyChanged(); } }
        public string Modifier2 { get => _modifier2; set { _modifier2 = value; OnPropertyChanged(); } }
        public string Key { get => _key; set { _key = value; OnPropertyChanged(); } }
        public string MouseButton { get => _mouseButton; set { _mouseButton = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public MouseButtons GetParsedMouseButton()
        {
            if (MouseButton == "Middle") return MouseButtons.Middle;
            if (MouseButton == "Left") return MouseButtons.Left;
            if (MouseButton == "XButton1") return MouseButtons.XButton1;
            if (MouseButton == "XButton2") return MouseButtons.XButton2;
            if (MouseButton == "None") return MouseButtons.None;
            return MouseButtons.Right;
        }

        public Keys GetParsedKey()
        {
            if (Enum.TryParse<Keys>(Key, out var k)) return k;
            return Keys.None;
        }

        public bool IsModifierMatch(Func<int, short> getAsyncKeyState)
        {
            bool isCtrlDown = (getAsyncKeyState(0x11) & 0x8000) != 0; // VK_CONTROL
            bool isShiftDown = (getAsyncKeyState(0x10) & 0x8000) != 0; // VK_SHIFT
            bool isAltDown = (getAsyncKeyState(0x12) & 0x8000) != 0; // VK_MENU
            bool isWinLDown = (getAsyncKeyState(0x5B) & 0x8000) != 0; // VK_LWIN
            bool isWinRDown = (getAsyncKeyState(0x5C) & 0x8000) != 0; // VK_RWIN

            bool reqCtrl = Modifier1 == "Ctrl" || Modifier2 == "Ctrl";
            bool reqShift = Modifier1 == "Shift" || Modifier2 == "Shift";
            bool reqAlt = Modifier1 == "Alt" || Modifier2 == "Alt";
            bool reqWinL = Modifier1 == "WinL" || Modifier2 == "WinL";
            bool reqWinR = Modifier1 == "WinR" || Modifier2 == "WinR";

            return isCtrlDown == reqCtrl &&
                   isShiftDown == reqShift &&
                   isAltDown == reqAlt &&
                   isWinLDown == reqWinL &&
                   isWinRDown == reqWinR;
        }
    }

    public class AppSettings : INotifyPropertyChanged
    {
        private bool _silentStart = false;
        private bool _autoStart = false;
        private bool _runAsAdmin = false;

        private HotkeyConfig _menuHotkey = new HotkeyConfig { Modifier1 = "Ctrl", Modifier2 = "Shift", MouseButton = "Right" };
        private HotkeyConfig _transparencyHotkey = new HotkeyConfig { Modifier1 = "Ctrl", Modifier2 = "Shift", MouseButton = "None" };
        private HotkeyConfig _widthHotkey = new HotkeyConfig { Modifier1 = "Shift", Modifier2 = "Alt", MouseButton = "None" };
        private HotkeyConfig _heightHotkey = new HotkeyConfig { Modifier1 = "Ctrl", Modifier2 = "Alt", MouseButton = "None" };

        private bool _enableNumpadAlign = true;
        private bool _enableNumpadMove = true;
        private bool _enableTransparencyWheel = true;
        private bool _enableSizeWheel = true;

        public HotkeyConfig MenuHotkey { get => _menuHotkey; set { _menuHotkey = value; OnPropertyChanged(); } }
        public HotkeyConfig TransparencyHotkey { get => _transparencyHotkey; set { _transparencyHotkey = value; OnPropertyChanged(); } }
        public HotkeyConfig WidthHotkey { get => _widthHotkey; set { _widthHotkey = value; OnPropertyChanged(); } }
        public HotkeyConfig HeightHotkey { get => _heightHotkey; set { _heightHotkey = value; OnPropertyChanged(); } }

        public bool EnableNumpadAlign { get => _enableNumpadAlign; set { _enableNumpadAlign = value; OnPropertyChanged(); } }
        public bool EnableNumpadMove { get => _enableNumpadMove; set { _enableNumpadMove = value; OnPropertyChanged(); } }
        public bool EnableTransparencyWheel { get => _enableTransparencyWheel; set { _enableTransparencyWheel = value; OnPropertyChanged(); } }
        public bool EnableSizeWheel { get => _enableSizeWheel; set { _enableSizeWheel = value; OnPropertyChanged(); } }

        public ObservableCollection<WindowSizeItem> CustomWindowSizes { get; set; } = new ObservableCollection<WindowSizeItem>();
        public ObservableCollection<string> BlacklistProcesses { get; set; } = new ObservableCollection<string>();

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