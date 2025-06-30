using MarketAssistant.Infrastructure;
using MarketAssistant.Services;
using MarketAssistant.WinUI.Services;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.UI.Windowing;

namespace MarketAssistant.WinUI
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();

            builder
                .UseSharedMauiApp()
                .ConfigureLifecycleEvents(events =>
                {
                    events.AddWindows(windowsLifecycleBuilder =>
                    {
                        windowsLifecycleBuilder.OnWindowCreated(window =>
                        {
                            var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
                            var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle);
                            var appWindow = AppWindow.GetFromWindowId(id);
                            //appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                            // Get the display area for the window
                            DisplayArea displayArea = DisplayArea.GetFromWindowId(id, DisplayAreaFallback.Primary);

                            if (displayArea != null)
                            {
                                // Get the usable work area dimensions
                                var workArea = displayArea.WorkArea;

                                // Set window size to cover the working area
                                appWindow.Resize(new Windows.Graphics.SizeInt32(workArea.Width, workArea.Height));

                                // Move window to the top-left of the work area
                                appWindow.Move(new Windows.Graphics.PointInt32(workArea.X, workArea.Y));
                            }

                            if (appWindow.Presenter is OverlappedPresenter overlappedPresenter)
                            {
                                overlappedPresenter.IsMaximizable = false;
                                overlappedPresenter.IsResizable = false;
                            }
                        });
                    });
                });

            builder.Services.AddSingleton<IBrowserService, BrowserService>();
            builder.Services.AddSingleton<ISystemTrayService, SystemTrayService>();
            builder.Services.AddSingleton<IWindowManagementService, WindowManagementService>();

            return builder.Build();
        }
    }
}
