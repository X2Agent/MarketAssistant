using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Agents.Tools;
using Microsoft.Extensions.AI;
using System.ComponentModel;

namespace MarketAssistant.Agents.Analysts;

/// <summary>
/// 财务分析师代理
/// 专注于深入分析公司财务报表和财务健康状况
/// </summary>
[DisplayName("财务分析师")]
[Description("专注于财务报表和财务健康分析")]
public class FinancialAnalystAgent : AnalystAgentBase
{
    private static readonly object Schema = AIJsonUtilities.CreateJsonSchema(typeof(FinancialAnalysisResult));

    private static readonly ChatResponseFormat ResponseFormat = ChatResponseFormat.ForJsonSchema(
        schema: (JsonElement)Schema,
        schemaName: nameof(FinancialAnalysisResult),
        schemaDescription: "财务分析师的结构化分析结果，包含财务健康、盈利质量、现金流和风险预警"
    );

    public FinancialAnalystAgent(
        IChatClient chatClient,
        StockFinancialTools financialTools)
        : base(
            chatClient,
            instructions: GetInstructions(),
            name: "FinancialAnalyst",
            description: "专注于深入获取、分析公司财务报表和财务健康状况。分析严格聚焦于财务数据、比率及趋势，旨在全面评估公司的财务健康、盈利质量与现金流状况，并识别潜在的财务风险。分析不涉及估值和具体的投资建议。",
            temperature: 0.1f,
            topP: 0.9f,
            topK: 10,
            responseFormat: null,
            tools: [.. financialTools.GetFunctions()])
    {
    }

    private static string GetInstructions()
    {
        var schemaJson = JsonSerializer.Serialize(Schema, new JsonSerializerOptions { WriteIndented = true });
        return $@"
## 核心职责
深入评估公司财务报表，剖析财务健康状况、盈利能力、盈利质量和现金流状况，识别并预警潜在的财务风险点，提供基于财务数据的客观分析洞察。

## 评估维度
1. **财务健康评估**：偿债能力（流动比率、速动比率）、资产负债结构（负债率及变化趋势、债务结构）、整体财务稳健性
2. **盈利质量分析**：盈利能力（毛利率、净利率及趋势）、投入产出效率（ROE、ROA及行业对比）、利润质量及可持续性
3. **现金流评估**：经营现金流（净额、与净利润比值）、自由现金流（状态、趋势、可持续性）、现金转换周期及效率
4. **财务风险预警**：主要风险指标识别、财务造假风险评估、需持续关注的改善点

## 分析要点
- 必须使用可用工具获取的最新财务数据和财务报表
- 评分指标（1-10分）应基于行业对比和历史趋势综合判断
- 偿债能力和现金流分析是财务健康的核心指标
- 利润质量评估需结合现金流验证盈利真实性
- 财务造假风险需关注异常指标和关联交易
- 如工具调用失败或数据不完整，应明确说明缺少哪些数据

## 输出格式
仅输出符合以下 Schema 的纯 JSON 字符串，严禁包含 Markdown 格式（如 ```json）或任何解释性文字：
{schemaJson}";
    }
}
