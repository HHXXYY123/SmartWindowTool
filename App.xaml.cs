using System.Configuration;
using System.Data;
using System.Windows;
using SmartWindowTool.Models;

namespace SmartWindowTool;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private MainWindow _mainWindow;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        // 加载设置以判断是否静默启动
        var settings = AppSettings.Load();

        _mainWindow = new MainWindow();
        
        if (!settings.SilentStart)
        {
            _mainWindow.Show();
        }
        else
        {
            // 对于静默启动，我们不调用 Show()，WPF 的窗体在未调用 Show 之前本身就是隐藏的。
            // 但如果需要在后台执行某些初始化（如创建托盘图标等，已经在 MainWindow 构造函数中完成）。
            // 只需要将其赋值给 MainWindow 即可，不显示出来就不会有黑色闪烁窗口。
        }
    }
}

