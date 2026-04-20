using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmartWindowTool.Models
{
    public class HotkeyProfile : INotifyPropertyChanged
    {
        private bool _isEnabled = true;
        private string _modifier1 = "None";
        private string _modifier2 = "None";
        private string _key1 = "None";
        private string _key2 = "None";
        private string _mouseButton = "None";

        public bool IsEnabled { get => _isEnabled; set { _isEnabled = value; OnPropertyChanged(); } }
        public string Modifier1 { get => _modifier1; set { _modifier1 = value; OnPropertyChanged(); } }
        public string Modifier2 { get => _modifier2; set { _modifier2 = value; OnPropertyChanged(); } }
        public string Key1 { get => _key1; set { _key1 = value; OnPropertyChanged(); } }
        public string Key2 { get => _key2; set { _key2 = value; OnPropertyChanged(); } }
        public string MouseButton { get => _mouseButton; set { _mouseButton = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}