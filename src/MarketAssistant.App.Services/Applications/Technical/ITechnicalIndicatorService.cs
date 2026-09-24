using MarketAssistant.Applications.Charts.Models;
using MarketAssistant.Applications.Technical.Models;

namespace MarketAssistant.Applications.Technical;

/// <summary>
/// 技术指标服务（按市场 Keyed 注册）：基于该市场 K 线在本地统一计算 MA/MACD/BOLL/KDJ，
/// 避免 UI 与 Agent 工具各自实现一套公式。K 线数据为空时抛出异常。
/// </summary>
public interface ITechnicalIndicatorService
{
    Task<TechnicalIndicatorSnapshot> GetSnapshotAsync(
        string code,
        KLineType kLineType = KLineType.Daily,
        int barCount = 250,
        CancellationToken cancellationToken = default);
}
