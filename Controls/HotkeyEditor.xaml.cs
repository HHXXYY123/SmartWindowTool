using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace SmartWindowTool.Controls
{
    public partial class HotkeyEditor : UserControl, INotifyPropertyChanged
    {
        public List<string> AvailableKeys { get; } = new List<string> {
            "None", "Backspace", "Tab", "Enter", "Pause", "Caps Lock", "Esc", "Space",
            "Page Up", "Page Down", "End", "Home", "Left Arrow", "Up Arrow", "Right Arrow", "Down Arrow",
            "Print Screen", "Ins", "Del", "0", "1", "2", "3", "4", "5", "6", "7", "8", "9",
            "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
            "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12"
        };

        public static readonly DependencyProperty IsMouseWheelModeProperty =
            DependencyProperty.Register("IsMouseWheelMode", typeof(bool), typeof(HotkeyEditor), new PropertyMetadata(false, OnIsMouseWheelModeChanged));

        public bool IsMouseWheelMode
        {
            get { return (bool)GetValue(IsMouseWheelModeProperty); }
            set { SetValue(IsMouseWheelModeProperty, value); }
        }

        public Visibility ShowMouseButton
        {
            get { return IsMouseWheelMode ? Visibility.Collapsed : Visibility.Visible; }
        }

        public Visibility ShowMouseWheel
        {
            get { return IsMouseWheelMode ? Visibility.Visible : Visibility.Collapsed; }
        }

        private static void OnIsMouseWheelModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HotkeyEditor editor)
            {
                // trigger a binding update
                var propChange = new System.ComponentModel.PropertyChangedEventArgs(nameof(ShowMouseButton));
                editor.PropertyChanged?.Invoke(editor, propChange);
                editor.PropertyChanged?.Invoke(editor, new System.ComponentModel.PropertyChangedEventArgs(nameof(ShowMouseWheel)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        public HotkeyEditor()
        {
            InitializeComponent();
        }
    }
}