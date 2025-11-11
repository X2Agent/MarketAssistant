using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using MarketAssistant.ViewModels;
using MarketAssistant.Views.Windows;
using Serilog;

namespace MarketAssistant;

public partial class App : Application
{
    public static IServiceProvider? ServiceProvider { get; private set; }
    private Window? _mainWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 配置依赖注入
        ServiceProvider = Program.ConfigureServices();

        // 初始化全局异常处理器
        Infrastructure.Core.GlobalExceptionHandler.Initialize(ServiceProvider);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();

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

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}