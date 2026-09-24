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
    // A 股暂无公开的实时五档/逐笔数据源，资产页不展示盘口面板
    public bool SupportsOrderBook => false;
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
