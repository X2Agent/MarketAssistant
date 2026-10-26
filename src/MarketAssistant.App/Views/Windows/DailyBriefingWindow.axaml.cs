using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MarketAssistant.ViewModels.Home;

namespace MarketAssistant.Views.Windows;

/// <summary>
/// 每日简报窗口：以弹窗形式展示 Markdown 全文（含重新生成与 Token 成本信息）。
/// </summary>
/// <remarks>
/// <para>
/// 由「7*24 小时快讯」卡片标题栏的入口按钮打开——简报与快讯同属资讯域
/// （前者是后者的 AI 解读），置于同一卡片入口语义自洽，也免去在首页为简报
/// 长期占据黄金位置所需的"关闭/次日复现"等额外状态。
/// </para>
/// <para>
/// 采用独立 Window 而非 Flyout：Flyout 在 Avalonia 中存在光标捕获、焦点与
/// 边缘裁剪问题，长 Markdown 内容表现不稳定；独立窗口是本项目既有成熟模式
/// （参见 <see cref="MessageDialogWindow"/>、<see cref="InputDialogWindow"/>）。
/// </para>
/// <para>
/// 通过 DataContext 绑定 <see cref="DailyBriefingViewModel"/>，而非在 code-behind
/// 里 FindControl 赋值：重新生成后正文与成本信息会随绑定自动刷新，无需手工同步。
/// </para>
/// </remarks>
public partial class DailyBriefingWindow : Window
{
    public DailyBriefingWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}