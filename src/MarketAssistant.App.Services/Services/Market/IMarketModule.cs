namespace MarketAssistant.Services.Market;

/// <summary>
/// 市场模块契约——每个市场实现一个模块类，集中管理该市场所有的 Keyed DI 注册。
/// 新增市场时只需创建新的 <see cref="IMarketModule"/> 实现并加入模块列表，无需修改分散的注册方法。
/// </summary>
public interface IMarketModule
{
    /// <summary>市场类型标识</summary>
    MarketType MarketType { get; }

    /// <summary>向 <paramref name="services"/> 注册该市场的所有依赖</summary>
    void Register(IServiceCollection services);
}
