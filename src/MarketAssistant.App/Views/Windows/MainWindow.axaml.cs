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
        e.Cancel = true;
        Hide();

        base.OnClosing(e);
    }
}