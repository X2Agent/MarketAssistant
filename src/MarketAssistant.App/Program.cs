using Avalonia;
using MarketAssistant.Rag.Extensions;
using MarketAssistant.Services;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

            // 日志配置与业务层复用同一个设置实例，避免同一进程重复打开安全存储
            // 或并行写入同一设置文件。后续同类型注册覆盖 AddBusinessServices 的默认注册。
            var userSettingService = new UserSettingService();
            services.AddLogging(builder => builder.ConfigureLogging(userSettingService));

            // 注册应用程序业务服务，并用启动期实例替换默认设置服务注册。
            services.AddApplicationServices();
            services.RemoveAll<IUserSettingService>();
            services.AddSingleton<IUserSettingService>(userSettingService);

            // 注册ViewModels
            services.AddViewModels();

            return services.BuildServiceProvider();
        }
    }
}
