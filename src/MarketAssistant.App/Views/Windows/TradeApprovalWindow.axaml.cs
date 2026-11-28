using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MarketAssistant.Services.Dialog;

namespace MarketAssistant.Views.Windows;

/// <summary>
/// 自动交易批准窗口。AXAML 负责布局，code-behind 负责倒计时展示、默认焦点与结果回传。
/// 只有点击“批准”才会把 <see cref="Approved"/> 置为 true，其余关闭方式一律视为拒绝。
/// </summary>
public partial class TradeApprovalWindow : Window
{
    private readonly DispatcherTimer _countdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTimeOffset _deadline;

    /// <summary>用户是否点击了“批准”。</summary>
    public bool Approved { get; private set; }

    public TradeApprovalWindow()
    {
        InitializeComponent();
        _countdownTimer.Tick += (_, _) => UpdateCountdown();
        Opened += OnOpened;
        Closed += (_, _) => _countdownTimer.Stop();
    }

    /// <summary>设置待批准的订单信息，并按截止时间初始化倒计时。</summary>
    public void SetContent(TradeApprovalRequest request)
    {
        DataContext = request;
        _deadline = request.Deadline;

        var countdownBar = this.FindControl<ProgressBar>("CountdownBar")!;
        countdownBar.Maximum = Math.Max(1, (request.Deadline - DateTimeOffset.UtcNow).TotalSeconds);
        UpdateCountdown();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _countdownTimer.Start();

        // 以 Tab 方式取焦点才会触发 :focus-visible 焦点环，让用户看见回车默认落在“拒绝”
        this.FindControl<Button>("RejectButton")!.Focus(NavigationMethod.Tab);
    }

    private void UpdateCountdown()
    {
        var remaining = Math.Max(0, Math.Ceiling((_deadline - DateTimeOffset.UtcNow).TotalSeconds));
        this.FindControl<ProgressBar>("CountdownBar")!.Value = remaining;
        this.FindControl<TextBlock>("CountdownText")!.Text = remaining > 0
            ? $"还剩 {remaining:0} 秒，超时将自动拒绝"
            : "已超时，正在自动拒绝";
    }

    private void OnApproveClick(object? sender, RoutedEventArgs e)
    {
        Approved = true;
        Close();
    }

    private void OnRejectClick(object? sender, RoutedEventArgs e) => Close();
}
