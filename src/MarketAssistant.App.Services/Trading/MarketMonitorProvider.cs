using Microsoft.Extensions.DependencyInjection;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 基于 DI 容器的市场监控器提供者实现，首次调用时才触发 MarketMonitor 单例构造。
/// </summary>
public sealed class MarketMonitorProvider : IMarketMonitorProvider
{
    private readonly IServiceProvider _serviceProvider;

    public MarketMonitorProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public MarketMonitor GetMonitor()
        => _serviceProvider.GetRequiredService<MarketMonitor>();
}
