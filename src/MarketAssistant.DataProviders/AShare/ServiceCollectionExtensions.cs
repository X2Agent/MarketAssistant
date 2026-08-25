using Microsoft.Extensions.DependencyInjection;

namespace MarketAssistant.DataProviders.AShare;

/// <summary>
/// 注册 A 股数据客户端（财联社行情/搜索、智兔市场数据、东方财富新闻、新浪资金流）。
/// 由宿主层的 AddBusinessServices 调用；HttpClient 命名注册仍留在
/// 宿主的 AddHttpClientsCore（弹性管线集中在宿主配置），本层通过
/// IHttpClientFactory + 现有名字消费。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 A 股外部数据客户端。
    /// </summary>
    public static IServiceCollection AddAShareDataProviders(this IServiceCollection services)
    {
        services.AddSingleton<ClsQuoteClient>();
        services.AddSingleton<ZhiTuMarketClient>();
        services.AddSingleton<EastMoneyNewsClient>();
        services.AddSingleton<SinaFundFlowClient>();
        return services;
    }
}