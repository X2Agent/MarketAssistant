using Microsoft.Extensions.DependencyInjection;

namespace MarketAssistant.DataProviders.Web3;

/// <summary>
/// 注册 Web3 / 链上数据客户端（DexScreener 多链 DEX 行情、GoPlus 安全审计）。
/// HttpClient 命名注册（"DexScreener" / "GoPlus"）集中在宿主层 AddHttpClientsCore，
/// 本层通过 IHttpClientFactory + 现有名字消费，与 AShare 子目录模式一致。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Web3 / 链上数据客户端。
    /// </summary>
    public static IServiceCollection AddWeb3DataProviders(this IServiceCollection services)
    {
        services.AddSingleton<DexScreenerClient>();
        services.AddSingleton<GoPlusSecurityClient>();
        return services;
    }
}