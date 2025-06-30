using MarketAssistant.Infrastructure;
using MarketAssistant.Mac.Services;
using MarketAssistant.Services;
using Microsoft.Maui.LifecycleEvents;

namespace MarketAssistant.Mac
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                })
                .ConfigureLifecycleEvents(events =>
                {
                    events.AddiOS(ios => ios
                        .OnActivated((app) => LogEvent("OnActivated"))
                        .OnResignActivation((app) => LogEvent("OnResignActivation"))
                        .DidEnterBackground((app) => LogEvent("DidEnterBackground"))
                        .WillEnterForeground((app) => LogEvent("WillEnterForeground"))
                        .WillTerminate((app) => LogEvent("WillTerminate"))
                        .FinishedLaunching((app, launchOptions) =>
                        {
                            LogEvent("FinishedLaunching");
                            return true;
                        }));
                });

            // 注册服务
            builder.Services.AddSingleton<IBrowserService, BrowserService>();
            builder.Services.AddSingleton<ISystemTrayService, SystemTrayService>();
            builder.Services.AddSingleton<IWindowManagementService, WindowManagementService>();


            return builder.Build();
        }

        static void LogEvent(string eventName, string type = null)
        {
            System.Diagnostics.Debug.WriteLine($"Lifecycle event: {eventName}{(type == null ? string.Empty : $" ({type})")}");
        }
    }
}
