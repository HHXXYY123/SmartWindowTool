using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using SmartWindowTool.Core;

namespace SmartWindowTool.Views
{
    public partial class FrozenImageWindow : Window
    {
        public FrozenImageWindow(BitmapSource image, int x, int y, int width, int height)
        {
            InitializeComponent();
            
            FrozenImage.Source = image;
            
            // Adjust size to physical bounds according to DPI
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            this.Left = x / dpi.DpiScaleX;
            this.Top = y / dpi.DpiScaleY;
            this.Width = width / dpi.DpiScaleX;
            this.Height = height / dpi.DpiScaleY;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                this.Close();
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Optional: Close on click? The user said "类似截图悬浮看完后再按 ESC 取消"
            // But we can let them drag it!
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
            else if (e.ChangedButton == MouseButton.Right)
            {
                this.Close();
            }
        }
    }
}