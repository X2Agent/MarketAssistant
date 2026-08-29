using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using MarketAssistant.Applications.PriceAlert;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Settings;
using MarketAssistant.ViewModels;
using MarketAssistant.Views.Windows;
using Microsoft.Extensions.Logging;
using Serilog;

namespace MarketAssistant;

public partial class App : Application
{
    // 应用启动期间用于解析少量根服务（异常处理器、主题设置、主窗口 ViewModel），
    // 业务层应通过构造函数注入获取依赖，不应直接访问此属性。
    public static IServiceProvider? ServiceProvider { get; private set; }
    private Window? _mainWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ServiceProvider = Program.ConfigureServices();

        // 初始化全局异常处理器（由 DI 创建实例，避免静态工厂自行 new）
        GlobalExceptionHandler.Initialize(
            ServiceProvider.GetRequiredService<GlobalExceptionHandler>());

        // 激活价格预警服务：后台完成规则加载和行情订阅；服务内部会让后续写操作等待初始化完成。
        // 显式观察故障：不观察的话异常只会在未来某次 GC 时经由 UnobservedTaskException 弹出，
        // 时机不定且用户无从知道价格告警已整体失效
        var priceAlertService = ServiceProvider.GetRequiredService<PriceAlertService>();
        _ = InitializePriceAlertServiceAsync(priceAlertService);

        // 激活 HITL 交易确认服务：DI 单例是惰性创建的，仅注册不会实例化，
        // 必须显式解析一次让构造函数完成对 TradeExecutor.ConfirmationRequested 的订阅，
        // 否则自动交易的超阈值订单会因无订阅者被静默拒绝
        ServiceProvider.GetRequiredService<MarketAssistant.Services.Trading.TradeConfirmationService>();

        var settingService = ServiceProvider.GetRequiredService<IUserSettingService>();
        RequestedThemeVariant = settingService.CurrentSetting.ThemeMode switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindowViewModel = ServiceProvider.GetRequiredService<MainWindowViewModel>();
            _mainWindow = new MainWindow
            {
                DataContext = mainWindowViewModel,
            };
            desktop.MainWindow = _mainWindow;

            // 配置关闭行为：最小化到托盘而不是退出
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            desktop.Exit += OnApplicationExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 初始化价格预警服务并显式观察失败，失败时记录日志并通知用户
    /// </summary>
    private static async Task InitializePriceAlertServiceAsync(PriceAlertService priceAlertService)
    {
        try
        {
            await priceAlertService.InitializeAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "价格预警服务初始化失败，价格告警功能不可用");
            try
            {
                var notificationService = ServiceProvider?.GetRequiredService<Services.Notification.INotificationService>();
                // NotificationService 内部投递到 UI 线程，后台线程调用安全
                notificationService?.ShowWarning("价格预警服务启动失败，告警功能暂不可用，请重启应用重试");
            }
            catch (Exception notifyEx)
            {
                Log.Error(notifyEx, "价格预警服务初始化失败的通知发送失败");
            }
        }
    }

    private void OnApplicationExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        try
        {
            GlobalExceptionHandler.Cleanup();

            // 释放根 DI 容器：触发容器创建的全部 IDisposable 单例的释放
            // （ChatClientFactory 缓存的 IChatClient、限流器、SQLite 服务等）。
            // 必须在关闭日志之前执行，保证释放过程中仍可写日志。
            (ServiceProvider as IDisposable)?.Dispose();
            ServiceProvider = null;

            Log.CloseAndFlush();
        }
        catch (Exception ex)
        {
            // 尽最大努力记录退出时的错误
            Console.Error.WriteLine($"应用退出清理时发生错误: {ex}");
        }
    }

    /// <summary>
    /// 托盘图标点击事件（双击或单击）
    /// </summary>
    private void TrayIcon_Clicked(object? sender, EventArgs e)
    {
        ShowMainWindow();
    }

    private void ShowMainWindow_Click(object? sender, EventArgs e)
    {
        ShowMainWindow();
    }

    private void Exit_Click(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void ShowMainWindow()
    {
        if (_mainWindow != null)
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }
    }
}