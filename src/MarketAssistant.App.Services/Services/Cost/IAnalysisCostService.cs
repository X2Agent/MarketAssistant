using MarketAssistant.Agents.TokenManagement;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Settings;

namespace MarketAssistant.Services.Cost;

/// <summary>
/// 分析成本估算服务：把 <see cref="AnalysisTokenUsage"/> 的各分析师 Token 消耗
/// 解析为本次实际使用的模型，乘用户维护的模型价格表得出金额。
/// </summary>
public interface IAnalysisCostService
{
    /// <summary>
    /// 估算一次分析的成本。价格表未覆盖的模型按"未定价"处理（计入下界标记，不计金额）。
    /// </summary>
    ModelCostEstimate Estimate(AnalysisTokenUsage? usage);

    /// <summary>成本展示文本；无有效金额时返回空串。</summary>
    string FormatCost(AnalysisTokenUsage? usage);
}
