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
        // 配置依赖注入
        ServiceProvider = Program.ConfigureServices();

        // 初始化全局异常处理器（由 DI 创建实例，避免静态工厂自行 new）
        GlobalExceptionHandler.Initialize(
            ServiceProvider.GetRequiredService<GlobalExceptionHandler>());

        // 激活价格预警服务：异步加载规则 + 启动后台监控
        var priceAlertService = ServiceProvider.GetRequiredService<PriceAlertService>();
        _ = priceAlertService.InitializeAsync();

        // 应用保存的主题
        var settingService = ServiceProvider.GetRequiredService<IUserSettingService>();
        RequestedThemeVariant = settingService.CurrentSetting.ThemeMode switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 使用DI容器创建MainWindowViewModel
            var mainWindowViewModel = ServiceProvider.GetRequiredService<MainWindowViewModel>();
            _mainWindow = new MainWindow
            {
                DataContext = mainWindowViewModel,
            };
            desktop.MainWindow = _mainWindow;

            // 配置关闭行为：最小化到托盘而不是退出
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // 订阅应用退出事件，进行资源清理
            desktop.Exit += OnApplicationExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 应用退出事件处理
    /// </summary>
    private void OnApplicationExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        try
        {
            // 清理全局异常处理器
            GlobalExceptionHandler.Cleanup();

            // 刷新并关闭日志
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

    /// <summary>
    /// 显示主窗口菜单项点击事件
    /// </summary>
    private void ShowMainWindow_Click(object? sender, EventArgs e)
    {
        ShowMainWindow();
    }

    /// <summary>
    /// 退出菜单项点击事件
    /// </summary>
    private void Exit_Click(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    /// <summary>
    /// 显示主窗口
    /// </summary>
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