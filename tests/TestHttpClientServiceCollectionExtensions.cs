using MarketAssistant.Services;
using Microsoft.Extensions.DependencyInjection;

namespace TestMarketAssistant;

/// <summary>
/// 测试用 HttpClient 注册，直接复用生产配置（含 Resilience Handler）
/// </summary>
internal static class TestHttpClientServiceCollectionExtensions
{
    public static IServiceCollection AddTestMarketDataHttpClients(this IServiceCollection services)
    {
        return services.AddNamedMarketHttpClients();
    }
}
