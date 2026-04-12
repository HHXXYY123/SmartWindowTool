using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmartWindowTool.Models
{
    public class HiddenWindowInfo : INotifyPropertyChanged
    {
        private IntPtr _hwnd;
        private string _title;
        private string _className;
        private DateTime _hiddenAt;
        private bool _isClickThrough;
        private bool _isTray;

        public IntPtr Hwnd
        {
            get => _hwnd;
            set
            {
                _hwnd = value;
                OnPropertyChanged();
            }
        }

        public string Title
        {
            get => _title;
            set
            {
                _title = value;
                OnPropertyChanged();
            }
        }

        public string ClassName
        {
            get => _className;
            set
            {
                _className = value;
                OnPropertyChanged();
            }
        }

        public DateTime HiddenAt
        {
            get => _hiddenAt;
            set
            {
                _hiddenAt = value;
                OnPropertyChanged();
            }
        }

        public bool IsClickThrough
        {
            get => _isClickThrough;
            set
            {
                _isClickThrough = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayText));
            }
        }

        public bool IsTray
        {
            get => _isTray;
            set
            {
                _isTray = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayText));
            }
        }

        public string DisplayText 
        {
            get
            {
                string text = string.IsNullOrWhiteSpace(Title) ? $"[{ClassName}] (无标题)" : Title;
                if (IsClickThrough)
                {
                    text += " [已开启鼠标穿透]";
                }
                if (IsTray)
                {
                    text += " [已最小化到托盘]";
                }
                return text;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}