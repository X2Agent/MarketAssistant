using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using MarketAssistant.ViewModels;

namespace MarketAssistant.Views.Windows;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SizeChanged += OnShellSizeChanged;

        // Esc 返回：键盘快捷键走代码而非 KeyBinding，因为需要先判断焦点是否在输入控件上——
        // 用户在搜索框/表单里按 Esc 的本意是「取消本次输入」，此时跳走页面会丢失正在编辑的内容。
        // 用冒泡阶段：先让聚焦控件自行消费（TextBox 等处理后会置 Handled），未消费才轮到页面返回。
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Bubble);
    }

    /// <summary>
    /// 窗口级 Esc：可返回时退回上一页。
    /// 焦点位于文本输入类控件时一律让位（TextBox / AutoCompleteBox / ComboBox 等都会自行消费 Esc）；
    /// 另有 Ctrl+M 切换市场的先例，故快捷键统一在窗口层处理。
    /// </summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.Handled)
        {
            return;
        }

        if (DataContext is not MainWindowViewModel viewModel || !viewModel.CanGoBack)
        {
            return;
        }

        // 输入中按 Esc 属于「取消编辑」，不能让页面跳走
        if (FocusManager?.GetFocusedElement() is TextBox or AutoCompleteBox or ComboBox)
        {
            return;
        }

        viewModel.GoBackCommand.Execute(null);
        e.Handled = true;
    }

    /// <summary>
    /// 窄窗自适应：桌面/平板保持文字导航，窗口收窄到断点以下时收敛为图标轨
    /// （文字隐藏，图标居中），把横向空间让给内容区。
    /// 通过切换 ShellGrid 上的 compact 类由样式驱动，不逐个遍历子元素。
    /// 断点与两档宽度全部读 Spacing.axaml 令牌，这里不写字面量；
    /// 令牌缺失（设计器预览等无 App 资源的环境）时回退到同一组默认值。
    /// </summary>
    private void OnShellSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (this.FindControl<Grid>("ShellGrid") is not { } grid ||
            grid.ColumnDefinitions.Count == 0)
        {
            return;
        }

        var compact = e.NewSize.Width < ReadDouble("CompactWindowBreakpoint", 1024);
        var target = compact
            ? ReadDouble("NavRailCollapsedWidth", 72)
            : ReadDouble("NavRailWidth", 240);

        if (Math.Abs(grid.ColumnDefinitions[0].Width.Value - target) > 0.5)
        {
            grid.ColumnDefinitions[0].Width = new GridLength(target);
        }

        if (compact == grid.Classes.Contains("compact"))
        {
            return;
        }

        if (compact)
        {
            grid.Classes.Add("compact");
        }
        else
        {
            grid.Classes.Remove("compact");
        }
    }

    private double ReadDouble(string key, double fallback) =>
        Application.Current?.TryGetResource(key, ThemeVariant.Default, out var value) == true
            ? value switch
            {
                double number => number,
                GridLength length => length.Value,
                string text when double.TryParse(text, out var parsed) => parsed,
                _ => fallback,
            }
            : fallback;

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
