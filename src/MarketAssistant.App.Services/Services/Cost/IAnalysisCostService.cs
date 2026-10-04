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

    /// <summary>
    /// 按明确的 provider + model 计算单次非分析调用的成本（如每日简报这类裸
    /// <c>IChatClient</c> 调用，不经分析师维度）。价格表未命中该模型时返回空串，
    /// 调用方应据此隐去金额而不是展示错误的 0。
    /// </summary>
    /// <param name="providerId">服务商 ID；为空时按未定价处理。</param>
    /// <param name="modelId">模型 ID；为空时按未定价处理。</param>
    /// <param name="inputTokens">输入 Token 数。</param>
    /// <param name="outputTokens">输出 Token 数。</param>
    string FormatCostForModel(string? providerId, string? modelId, long inputTokens, long outputTokens);
}
