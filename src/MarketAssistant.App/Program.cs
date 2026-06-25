using Avalonia;
using MarketAssistant.Rag.Extensions;
using MarketAssistant.Services;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.Hosting;

namespace MarketAssistant
{
    internal sealed class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args) => BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();

        /// <summary>
        /// 配置依赖注入服务
        /// </summary>
        public static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            // ConfigureLogging 只需要在启动时读取一次日志路径，直接实例化即可，
            // 避免构建临时容器（捕获依赖问题）。
            // IUserSettingService 的正式 Singleton 由 AddAgentTools() 内部负责注册。
            services.AddLogging(builder => builder.ConfigureLogging(new UserSettingService()));

            // 注册应用程序业务服务
            services.AddApplicationServices();

            // 注册ViewModels
            services.AddViewModels();

            return services.BuildServiceProvider();
        }
    }
}
