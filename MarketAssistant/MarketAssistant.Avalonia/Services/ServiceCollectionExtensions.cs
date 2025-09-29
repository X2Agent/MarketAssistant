using Microsoft.Extensions.DependencyInjection;

namespace MarketAssistant.Avalonia.Services;

/// <summary>
/// 服务注册扩展
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册Avalonia平台服务
    /// </summary>
    public static IServiceCollection AddAvaloniaServices(this IServiceCollection services)
    {
        // 注册对话框服务
        services.AddSingleton<IDialogService, DialogService>();
        
        return services;
    }
}
