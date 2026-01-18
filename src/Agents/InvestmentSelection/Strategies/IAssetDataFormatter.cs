using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Agents.InvestmentSelection.Strategies;

/// <summary>
/// 资产数据格式化策略接口
/// 用于将筛选结果格式化为适合 AI 分析的 JSON 文本
/// </summary>
public interface IAssetDataFormatter
{
    /// <summary>
    /// 支持的市场类型
    /// </summary>
    MarketType SupportedMarketType { get; }

    /// <summary>
    /// 将资产列表格式化为 JSON 字符串，供 AI 分析使用
    /// </summary>
    string FormatAssetsForAnalysis(List<ScreenerStockInfo> assets);

    /// <summary>
    /// 获取市场特定的 AI 分析指令
    /// </summary>
    string GetAnalysisInstructions(bool isNewsAnalysis);
}
