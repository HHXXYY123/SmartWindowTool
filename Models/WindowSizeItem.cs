using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmartWindowTool.Models
{
    public class WindowSizeItem : INotifyPropertyChanged
    {
        private string _title;
        private int _width;
        private int _height;

        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        public int Width
        {
            get => _width;
            set { _width = value; OnPropertyChanged(); }
        }

        public int Height
        {
            get => _height;
            set { _height = value; OnPropertyChanged(); }
        }

        public WindowSizeItem()
        {
            // Parameterless constructor for JSON deserialization
        }

        public WindowSizeItem(string title, int width, int height)
        {
            Title = title;
            Width = width;
            Height = height;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}