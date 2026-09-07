namespace MarketAssistant.Services.Trading;

/// <summary>
/// 交易环境的持久化 key（internal，仅供本程序集交易持久化管线使用）。
/// 4 种交易模式各自独立的环境 key，确保现货实盘、现货 Demo、合约实盘、合约 Testnet
/// 的策略、交易记录、持仓、风控配置互不混淆。
/// </summary>
internal static class TradingEnvironmentKeys
{
    internal const string LiveSpot = "crypto-live-spot";
    internal const string LiveFutures = "crypto-live-futures";
    internal const string FuturesTestnet = "crypto-binance-futures-testnet";
    internal const string SpotDemo = "crypto-binance-spot-demo";
}
