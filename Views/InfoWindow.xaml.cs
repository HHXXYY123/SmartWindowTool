using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SmartWindowTool.Views
{
    public partial class InfoWindow : Window
    {
        private string _processPath;

        public InfoWindow(string handle, string title, string className, string pid, string processName, string processPath)
        {
            InitializeComponent();

            _processPath = processPath;

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

        /// <summary>
        /// 单击复制内容（无选中文本时）；双击选词或拖动选中不触发复制
        /// </summary>
        private void InfoTextBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                var tb = sender as TextBox;
                // 只有没有文本选中时才复制（拖动选择会产生选中区域）
                if (tb != null && tb.SelectionLength == 0 && !string.IsNullOrEmpty(tb.Text))
                {
                    try
                    {
                        Clipboard.SetText(tb.Text);
                    }
                    catch { }
                }
            }
            // ClickCount >= 2: TextBox 原生双击选词，不处理
        }

        private void OpenPathFolder_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_processPath) && _processPath != "Unknown" && _processPath != "Access Denied")
            {
                try
                {
                    Process.Start("explorer.exe", $"/select,\"{_processPath}\"");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"无法打开目录: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("无法获取有效的进程路径。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
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