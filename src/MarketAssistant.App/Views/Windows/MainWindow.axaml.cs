using Avalonia.Controls;

namespace MarketAssistant.Views.Windows;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 重写关闭事件，最小化到托盘而不是退出
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // 取消关闭操作
        e.Cancel = true;

        // 隐藏窗口到托盘
        Hide();

        base.OnClosing(e);
    }
}