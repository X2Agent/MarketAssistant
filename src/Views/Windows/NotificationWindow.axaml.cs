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
    private const int DisplayDuration = 3000;

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
                        iconBorder.Background = new SolidColorBrush(Color.Parse("#4CAF50"));
                        iconText.Text = "✓";
                        break;
                    case NotificationType.Error:
                        iconBorder.Background = new SolidColorBrush(Color.Parse("#F44336"));
                        iconText.Text = "✕";
                        break;
                    case NotificationType.Warning:
                        iconBorder.Background = new SolidColorBrush(Color.Parse("#FF9800"));
                        iconText.Text = "!";
                        break;
                    case NotificationType.Info:
                        iconBorder.Background = new SolidColorBrush(Color.Parse("#2196F3"));
                        iconText.Text = "i";
                        break;
                }
            }
        }
    }

    /// <summary>
    /// 显示通知（带动画）
    /// </summary>
    public async Task ShowNotificationAsync(int durationMs = DisplayDuration)
    {
        // 初始位置（屏幕右侧外）
        var screen = Screens.Primary;
        if (screen != null)
        {
            var workingArea = screen.WorkingArea;
            var startX = workingArea.Right;
            var finalX = workingArea.Right - Width - 16;
            var y = workingArea.Bottom - Height - 16;

            Position = new PixelPoint((int)startX, (int)y);
        }

        Show();

        // 滑入动画
        await SlideInAsync();

        // 显示一段时间
        await Task.Delay(durationMs);

        // 滑出动画
        await SlideOutAsync();

        Close();
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
            var x = workingArea.Right - Width - 16;
            var y = workingArea.Bottom - Height - 16;
            Position = new PixelPoint((int)x, (int)y);
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
        var endX = workingArea.Right - Width - 16;
        var y = workingArea.Bottom - Height - 16;

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

    /// <summary>
    /// 滑出动画
    /// </summary>
    private async Task SlideOutAsync()
    {
        var screen = Screens.Primary;
        if (screen == null) return;

        var workingArea = screen.WorkingArea;
        var startX = workingArea.Right - Width - 16;
        var endX = workingArea.Right;
        var y = workingArea.Bottom - Height - 16;

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
    /// 关闭按钮点击事件
    /// </summary>
    private void OnCloseButtonClick(object? sender, RoutedEventArgs e)
    {
        _ = SlideOutAsync().ContinueWith(_ => Dispatcher.UIThread.Post(() => Close()));
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

