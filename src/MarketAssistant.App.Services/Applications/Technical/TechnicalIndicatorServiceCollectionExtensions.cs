using MarketAssistant.Applications.Charts;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Technical;

/// <summary>
/// 技术指标服务的 DI 注册入口。指标计算复用目标市场已注册的 <see cref="IKLineService"/>，
/// 因此必须在同一市场模块内注册。
/// </summary>
public static class TechnicalIndicatorServiceCollectionExtensions
{
    public static IServiceCollection AddTechnicalIndicatorService(this IServiceCollection services, MarketType marketType)
    {
        services.AddKeyedSingleton<ITechnicalIndicatorService>(
            marketType,
            (sp, _) => new TechnicalIndicatorService(
                sp.GetRequiredKeyedService<IKLineService>(marketType),
                sp.GetRequiredService<ILogger<TechnicalIndicatorService>>()));

        return services;
    }
}
