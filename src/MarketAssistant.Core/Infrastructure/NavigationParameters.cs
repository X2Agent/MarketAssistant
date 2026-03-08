namespace MarketAssistant.Infrastructure;

/// <summary>
/// 股票导航参数
/// </summary>
/// <param name="StockCode">股票代码</param>
/// <param name="StockName">股票名称</param>
public record StockNavigationParameter(string StockCode, string StockName = "");
