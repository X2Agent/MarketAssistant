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

        // 绕过 Avalonia 上游缺陷：中文输入法按 Shift 提交拼音会重复插入文本，须在首个窗口显示前安装
        ImeCommitGuard.Install();

        // 后台服务延迟到后台线程激活：这批显式 resolve 只为让 DI 构造单例并完成事件订阅/启动后台循环，
        // 其中交易确认链（TradeConfirmationService → RoutingExchangeClient 全模式客户端
        // → TradingCredentialStore 的 PBKDF2 解密）在 UI 线程上耗时可达百毫秒级，放在首帧前会拖慢窗口显示。
        // 首帧后数百毫秒的延迟不影响任何功能：MarketMonitor 需用户在交易页显式开启、
        // PendingOrderTimeoutService 首扫在 15s 后、指标告警评估器首评在 30s 后。
        _ = Task.Run(ActivateBackgroundServicesAsync);

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
    /// 激活需显式 resolve 的后台服务单例（价格预警、告警中心、交易确认、指标告警、评级告警、限价单超时管理）。
    /// 在后台线程执行以脱离首帧关键路径；整块捕获避免与应用退出清理竞态（ServiceProvider 已释放/置空）时
    /// 异常无人观察，经由 UnobservedTaskException 在未来某次 GC 时才弹出。
    /// </summary>
    private static async Task ActivateBackgroundServicesAsync()
    {
        var serviceProvider = ServiceProvider;
        if (serviceProvider == null)
            return;

        try
        {
            // 激活价格预警服务：后台完成规则加载和行情订阅；服务内部会让后续写操作等待初始化完成。
            // 显式观察故障：失败时记录日志并通知用户，而不是静默失效
            var priceAlertService = serviceProvider.GetRequiredService<PriceAlertService>();
            await InitializePriceAlertServiceAsync(priceAlertService);

            // 初始化统一告警中心：建 alert_events 表，确保启动后首个告警可立即落库
            await InitializeAlertCenterServiceAsync(
                serviceProvider.GetRequiredService<MarketAssistant.Applications.AlertCenter.IAlertCenterService>());

            // 激活 HITL 交易确认服务：DI 单例是惰性创建的，仅注册不会实例化，
            // 必须显式解析一次让构造函数完成对 TradeExecutor.ConfirmationRequested 的订阅，
            // 否则自动交易的超阈值订单会因无订阅者被静默拒绝
            serviceProvider.GetRequiredService<MarketAssistant.Services.Trading.TradeConfirmationService>();

            // 激活指标类告警评估器：同样依赖显式解析启动周期评估循环
            serviceProvider.GetRequiredService<MarketAssistant.Applications.PriceAlert.IndicatorAlertEvaluator>();

            // 激活 AI 评级变化告警：订阅报告归档事件（ReportSaved）
            serviceProvider.GetRequiredService<MarketAssistant.Applications.Analysis.AnalysisRatingAlertService>();

            // 激活策略限价单超时管理：周期扫描超时未成交的限价单并撤单
            serviceProvider.GetRequiredService<MarketAssistant.Services.Trading.PendingOrderTimeoutService>();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "后台服务激活失败");
        }
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

    /// <summary>
    /// 初始化统一告警中心（建表），失败仅记录日志：告警写入会随首次调用重试
    /// </summary>
    private static async Task InitializeAlertCenterServiceAsync(
        MarketAssistant.Applications.AlertCenter.IAlertCenterService alertCenterService)
    {
        try
        {
            await alertCenterService.InitializeAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "统一告警中心初始化失败");
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