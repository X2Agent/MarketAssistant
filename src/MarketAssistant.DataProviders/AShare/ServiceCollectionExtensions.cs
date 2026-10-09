using Microsoft.Extensions.DependencyInjection;

namespace MarketAssistant.DataProviders.AShare;

/// <summary>
/// 注册 A 股数据客户端（财联社行情/搜索、智兔市场数据、东方财富新闻、新浪资金流、新浪指数行情、新浪 K 线）。
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
        services.AddSingleton<SinaIndexQuoteClient>();
        // 个股五档盘口/快照字段（资产页 A 股盘口面板）：与指数行情同源，复用 SinaHq 命名客户端
        services.AddSingleton<SinaStockQuoteClient>();
        // 个股 K 线（智兔不覆盖基金/ETF 时的兜底数据源）：复用 SinaKLine 命名客户端
        services.AddSingleton<SinaKLineClient>();
        return services;
    }
}