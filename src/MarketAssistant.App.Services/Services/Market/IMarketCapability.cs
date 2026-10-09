using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Services.Market;

/// <summary>
/// 市场能力声明接口，各市场实现声明自身支持的功能
/// </summary>
public interface IMarketCapability
{
    MarketType MarketType { get; }
    bool SupportsAIAnalysis { get; }
    bool SupportsKLine { get; }
    bool SupportsScreener { get; }
    bool SupportsRealtime { get; }
    bool SupportsNews { get; }
    bool SupportsTrading { get; }
    /// <summary>是否提供盘口深度（买卖档位）数据。</summary>
    bool SupportsOrderBook { get; }
    /// <summary>是否提供逐笔成交明细数据。</summary>
    bool SupportsTickTrades { get; }
}

public class AShareMarketCapability : IMarketCapability
{
    public MarketType MarketType => MarketType.AShare;
    public bool SupportsAIAnalysis => true;
    public bool SupportsKLine => true;
    public bool SupportsScreener => true;
    public bool SupportsRealtime => false;
    public bool SupportsNews => true;
    public bool SupportsTrading => false;
    // 新浪个股行情接口提供五档盘口（与顶栏指数同源）。
    public bool SupportsOrderBook => true;
    // 该接口不提供逐笔成交明细：字段 26..29 实为卖四/卖五档位，30 起为日期时间。
    // 早期据错误假设解析出的"逐笔"只是卖盘档位，已移除；A 股逐笔区块因此隐藏。
    public bool SupportsTickTrades => false;
}

public class CryptoMarketCapability : IMarketCapability
{
    public MarketType MarketType => MarketType.Crypto;
    public bool SupportsAIAnalysis => true;
    public bool SupportsKLine => true;
    public bool SupportsScreener => true;
    public bool SupportsRealtime => true;
    public bool SupportsNews => true;
    public bool SupportsTrading => true;
    public bool SupportsOrderBook => true;
    public bool SupportsTickTrades => true;
}
