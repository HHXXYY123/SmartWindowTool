using System.Windows;
using System.Windows.Controls;

namespace SmartWindowTool.Controls
{
    public partial class HotkeySelector : UserControl
    {
        public static readonly DependencyProperty HideMouseSelectorProperty =
            DependencyProperty.Register("HideMouseSelector", typeof(bool), typeof(HotkeySelector), new PropertyMetadata(false, OnHideMouseSelectorChanged));

        public bool HideMouseSelector
        {
            get { return (bool)GetValue(HideMouseSelectorProperty); }
            set { SetValue(HideMouseSelectorProperty, value); }
        }

        private static void OnHideMouseSelectorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HotkeySelector selector)
            {
                bool hide = (bool)e.NewValue;
                selector.CmbMouse.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
                selector.TxtMousePlus.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        public HotkeySelector()
        {
            InitializeComponent();
        }
    }
}