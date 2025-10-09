using Avalonia;
using MarketAssistant.Avalonia.Services;
using MarketAssistant.Services.Settings;
using MarketAssistant.Vectors.Extensions;
using Microsoft.Extensions.Hosting;

namespace MarketAssistant.Avalonia
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

            // 注册用户设置服务为单例（需要先注册以便获取日志路径）
            services.AddSingleton<IUserSettingService, UserSettingService>();

            // 构建一个临时 ServiceProvider 以便在配置日志之前获取用户设置
            using (var tempProvider = services.BuildServiceProvider())
            {
                var userSettingService = tempProvider.GetRequiredService<IUserSettingService>();

                // 配置日志
                services.AddLogging(builder => builder.ConfigureLogging(userSettingService));
            }

            // 注册基础服务（RAG、向量化等）
            services.AddRagServices();

            // 注册应用程序业务服务
            services.AddApplicationServices();

            // 注册ViewModels
            services.AddViewModels();

            return services.BuildServiceProvider();
        }
    }
}
