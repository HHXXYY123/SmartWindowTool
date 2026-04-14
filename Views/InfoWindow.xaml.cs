using System;
using System.Windows;

namespace SmartWindowTool.Views
{
    public partial class InfoWindow : Window
    {
        public InfoWindow(string handle, string title, string className, string pid, string processName, string processPath)
        {
            InitializeComponent();
            
            TxtHandle.Text = handle;
            TxtTitle.Text = title;
            TxtClass.Text = className;
            TxtPid.Text = pid;
            TxtProcessName.Text = processName;
            TxtProcessPath.Text = processPath;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void CopyAndCloseButton_Click(object sender, RoutedEventArgs e)
        {
            string info = $"窗口句柄 (Handle): {TxtHandle.Text}\n" +
                          $"窗口标题 (Title): {TxtTitle.Text}\n" +
                          $"窗口类名 (Class): {TxtClass.Text}\n" +
                          $"进程 ID (PID): {TxtPid.Text}\n" +
                          $"进程名称 (Process Name): {TxtProcessName.Text}\n" +
                          $"进程路径 (Process Path): {TxtProcessPath.Text}";
            
            try
            {
                Clipboard.SetText(info);
            }
            catch { }
            
            this.Close();
        }
    }
}