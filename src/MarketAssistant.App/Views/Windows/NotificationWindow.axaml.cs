using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace MarketAssistant.Views.Windows;

/// <summary>
/// 桌面通知窗口（桌面右下角，Topmost 模式）
/// </summary>
public partial class NotificationWindow : Window
{
    private const int AnimationDuration = 300;
    private const int DisplayDuration = 5000;
    private const int MinimumDisplayDuration = 3000;
    private const int DurationPerLine = 700;
    private const int MaximumDisplayDuration = 12000;
    private const double MarginFromEdge = 16;
    private const double StackGap = 8;

    private static readonly Color SuccessColor = Color.Parse("#4CAF50");
    private static readonly Color ErrorColor = Color.Parse("#F44336");
    private static readonly Color WarningColor = Color.Parse("#FF9800");
    private static readonly Color InfoColor = Color.Parse("#2196F3");

    // 堆叠槽位（0 = 底部基准位，向上依次偏移），由 NotificationService 分配
    private int _slot;
    private bool _closing;
    private readonly CancellationTokenSource _closeCts = new();

    public NotificationWindow()
    {
        InitializeComponent();

        // 设置窗口位置到屏幕右下角
        PositionWindow();

        // 绑定关闭按钮
        var closeButton = this.FindControl<Button>("CloseButton");
        if (closeButton != null)
        {
            closeButton.Click += OnCloseButtonClick;
        }
    }

    /// <summary>
    /// 设置堆叠槽位，由 <see cref="NotificationService"/> 在显示前分配
    /// </summary>
    public void SetStackSlot(int slot) => _slot = Math.Max(0, slot);

    /// <summary>
    /// 槽位回收后整体挪位（下方通知关闭后，上方通知顺次下移）
    /// </summary>
    public void MoveToSlot(int slot)
    {
        _slot = Math.Max(0, slot);
        var screen = Screens.Primary;
        if (screen == null) return;
        Position = new PixelPoint(Position.X, ComputeY(screen.WorkingArea));
    }

    private int ComputeY(PixelRect workingArea) =>
        (int)(workingArea.Bottom - Height - MarginFromEdge - _slot * (Height + StackGap));

    /// <summary>
    /// 设置通知消息
    /// </summary>
    public void SetMessage(string message, NotificationType type)
    {
        var messageText = this.FindControl<TextBlock>("MessageText");
        if (messageText != null)
        {
            messageText.Text = message;
        }

        var notificationBorder = this.FindControl<Border>("NotificationBorder");
        if (notificationBorder?.Child is Grid grid && grid.Children.Count > 0)
        {
            // 第一个子元素是图标 Border
            if (grid.Children[0] is Border iconBorder && iconBorder.Child is TextBlock iconText)
            {
                switch (type)
                {
                    case NotificationType.Success:
                        iconBorder.Background = new SolidColorBrush(SuccessColor);
                        iconText.Text = "✓";
                        break;
                    case NotificationType.Error:
                        iconBorder.Background = new SolidColorBrush(ErrorColor);
                        iconText.Text = "✕";
                        break;
                    case NotificationType.Warning:
                        iconBorder.Background = new SolidColorBrush(WarningColor);
                        iconText.Text = "!";
                        break;
                    case NotificationType.Info:
                        iconBorder.Background = new SolidColorBrush(InfoColor);
                        iconText.Text = "i";
                        break;
                }
            }
        }
    }

    /// <summary>
    /// 显示通知（带动画）。单一关闭路径：点击关闭按钮只取消等待，
    /// 滑出与 Close 统一由本方法收尾，避免双重动画/二次 Close。
    /// </summary>
    public async Task ShowNotificationAsync(int durationMs = DisplayDuration)
    {
        // 初始位置（屏幕右侧外）
        var screen = Screens.Primary;
        if (screen != null)
        {
            var workingArea = screen.WorkingArea;
            var startX = workingArea.Right;
            var y = ComputeY(workingArea);

            Position = new PixelPoint((int)startX, y);
        }

        Show();

        try
        {
            // 滑入动画
            await SlideInAsync();

            // 长文本按预计行数延长停留时间，避免用户尚未读完就消失。
            try
            {
                await Task.Delay(CalculateDisplayDuration(durationMs), _closeCts.Token);
            }
            catch (OperationCanceledException)
            {
                // 用户点击了关闭按钮：跳过剩余等待，由下方统一滑出
            }
        }
        finally
        {
            _closing = true;
            await SlideOutAsync();
            Close();
            _closeCts.Dispose();
        }
    }

    /// <summary>
    /// 定位窗口到屏幕右下角
    /// </summary>
    private void PositionWindow()
    {
        var screen = Screens.Primary;
        if (screen != null)
        {
            var workingArea = screen.WorkingArea;
            var x = workingArea.Right - Width - MarginFromEdge;
            Position = new PixelPoint((int)x, ComputeY(workingArea));
        }
    }

    /// <summary>
    /// 滑入动画
    /// </summary>
    private async Task SlideInAsync()
    {
        var screen = Screens.Primary;
        if (screen == null) return;

        var workingArea = screen.WorkingArea;
        var startX = workingArea.Right;
        var endX = workingArea.Right - Width - MarginFromEdge;
        var y = ComputeY(workingArea);

        var steps = 20;
        var stepDuration = AnimationDuration / steps;

        for (int i = 0; i <= steps; i++)
        {
            var progress = (double)i / steps;
            var easedProgress = EaseOutCubic(progress);
            var currentX = startX + (endX - startX) * easedProgress;

            Position = new PixelPoint((int)currentX, (int)y);
            await Task.Delay(stepDuration);
        }
    }

    private int CalculateDisplayDuration(int requestedDuration)
    {
        var messageText = this.FindControl<TextBlock>("MessageText")?.Text ?? string.Empty;
        var estimatedLines = Math.Max(1, (int)Math.Ceiling(messageText.Length / 28d));
        var contentDuration = estimatedLines * DurationPerLine;
        var displayDuration = Math.Max(MinimumDisplayDuration, Math.Max(requestedDuration, contentDuration));
        return Math.Min(MaximumDisplayDuration, displayDuration);
    }

    /// <summary>
    /// 滑出动画
    /// </summary>
    private async Task SlideOutAsync()
    {
        var screen = Screens.Primary;
        if (screen == null) return;

        var workingArea = screen.WorkingArea;
        var startX = workingArea.Right - Width - MarginFromEdge;
        var endX = workingArea.Right;
        var y = ComputeY(workingArea);

        var steps = 20;
        var stepDuration = AnimationDuration / steps;

        for (int i = 0; i <= steps; i++)
        {
            var progress = (double)i / steps;
            var easedProgress = EaseInCubic(progress);
            var currentX = startX + (endX - startX) * easedProgress;

            Position = new PixelPoint((int)currentX, (int)y);
            await Task.Delay(stepDuration);
        }
    }

    /// <summary>
    /// 缓动函数
    /// </summary>
    private double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);

    private double EaseInCubic(double t) => t * t * t;

    /// <summary>
    /// 关闭按钮点击事件：只取消显示等待，滑出与 Close 由 <see cref="ShowNotificationAsync"/> 统一收尾
    /// </summary>
    private void OnCloseButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_closing) return;
        _closing = true;
        _closeCts.Cancel();
    }
}

/// <summary>
/// 通知类型
/// </summary>
public enum NotificationType
{
    Success,
    Error,
    Warning,
    Info
}

