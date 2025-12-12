using MarketAssistant.Agents.Analysts.Attributes;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Agents.Tools;
using Microsoft.Extensions.AI;
using System.ComponentModel;

namespace MarketAssistant.Agents.Analysts;

/// <summary>
/// 市场情绪分析师代理
/// 专注于分析市场情绪、资金流向和投资者行为
/// </summary>
[DisplayName("市场情绪分析师")]
[Description("整合了行为金融分析师和市场分析师的功能")]
public class MarketSentimentAnalystAgent : AnalystAgentBase
{
    private static readonly object Schema = AIJsonUtilities.CreateJsonSchema(typeof(MarketSentimentAnalysisResult));

    private static readonly ChatResponseFormat ResponseFormat = ChatResponseFormat.ForJsonSchema(
        schema: (JsonElement)Schema,
        schemaName: nameof(MarketSentimentAnalysisResult),
        schemaDescription: "市场情绪分析师的结构化分析结果，包含市场情绪、资金流向、投资者行为和短期策略"
    );

    public MarketSentimentAnalystAgent(
        IChatClient chatClient,
        StockFinancialTools financialTools,
        MarketSentimentTools marketSentimentTools)
        : base(
            chatClient,
            instructions: GetInstructions(),
            name: "MarketSentimentAnalyst",
            description: "专注于分析市场情绪、资金流向和投资者行为。",
            temperature: 0.4f,
            topP: 0.7f,
            topK: 10,
            responseFormat: null,
            tools: [.. financialTools.GetFunctions(), .. marketSentimentTools.GetFunctions()])
    {
    }

    private static string GetInstructions()
    {
        var schemaJson = JsonSerializer.Serialize(Schema, new JsonSerializerOptions { WriteIndented = true });
        return $@"
## 核心职责
全面评估当前市场情绪与投资者心理状态，精准追踪资金流向与机构投资者行为，识别并解析投资者行为偏差与市场热点规律，预测短期市场波动并提供可操作的交易机会与策略。

## 评估维度
1. **市场情绪评估**：主导情绪及强度、恐慌与信心指标（VIX等）、投资者信心水平及变化、整体市场氛围及强度
2. **资金流向分析**：主力资金流向及金额、机构动向及持仓变化、北向资金流向及占比、融资融券变化及杠杆率
3. **投资者行为分析**：主要行为偏差及严重程度、散户特征及活跃度、机构行为一致性及主要动向、风险偏好及变化
4. **短期市场洞察与策略**：市场节奏判断、热点板块及持续性、短线机会识别、操作建议及仓位策略、最佳时机及价格区间、需规避的心理陷阱

## 分析要点
- 优先使用可用工具获取的资金流向数据、市场情绪指标、机构持仓数据
- 评分指标（1-10分）应基于历史数据对比和市场氛围综合判断
- 资金流向是市场情绪的重要验证指标，需关注连续性和金额规模
- 行为偏差分析需结合当前市场阶段和投资者特征
- 短期策略应明确具体的时间窗口和价格区间
- 心理陷阱识别有助于投资者避免情绪化决策
- 如缺乏数据，应明确说明并基于可用信息给出合理推断

## 输出格式
仅输出符合以下 Schema 的纯 JSON 字符串，严禁包含 Markdown 格式（如 ```json）或任何解释性文字：
{schemaJson}";
    }
}
