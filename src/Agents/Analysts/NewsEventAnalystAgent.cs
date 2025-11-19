using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Agents.Tools;
using Microsoft.Extensions.AI;

namespace MarketAssistant.Agents.Analysts;

/// <summary>
/// 新闻事件分析师代理
/// 专注于分析新闻事件、公告和突发事件对股票的影响
/// </summary>
public class NewsEventAnalystAgent : AnalystAgentBase
{
    public NewsEventAnalystAgent(
        IChatClient chatClient,
        StockNewsTools newsTools)
        : base(
            chatClient,
            instructions: GetInstructions(),
            name: "NewsEventAnalyst",
            description: "专注于分析新闻事件、公告和突发事件对股票的影响。",
            temperature: 0.2f,
            topP: 0.75f,
            topK: 10,
            responseFormat: ChatResponseFormat.ForJsonSchema(
                schema: AIJsonUtilities.CreateJsonSchema(typeof(NewsEventAnalysisResult)),
                schemaName: nameof(NewsEventAnalysisResult),
                schemaDescription: "新闻事件分析师的结构化分析结果，包含事件解读、影响评估和投资启示"
            ),
            tools: CreateTools(newsTools))
    {
    }

    private static string GetInstructions() => @"
## 核心职责
精准分析新闻事件对股票的短期与中期影响。分析聚焦于事件的真实性、重要性、市场影响和潜在的投资启示，严格避免技术面分析和不基于事件的长期投资建议。

## 数据获取与分析流程
使用可用的新闻获取工具获取与目标股票相关的聚合新闻要点。优先选择最相关且具有潜在影响力的2-3条新闻进行分析。

## 评估维度
1. **事件解读与定性**：事件类型分类、事件核心概要、信息来源及可信度、事件性质及重要性
2. **影响评估与市场反应**：基本面影响及逻辑、情绪影响及预期变化、影响范围及持续时长、市场预期反应及股价变化、资金流向预期及规模
3. **投资启示与建议**：投资影响评估及核心逻辑、应对策略建议及具体操作、需持续关注的重点、关键风险提示

## 分析要点
- 必须使用可用工具获取最新新闻数据和公告信息
- 评分指标（1-10分）应基于事件重要性、可信度和市场影响综合判断
- 区分事件的短期情绪影响和中长期基本面影响
- 信息来源的可信度直接影响事件分析的权重
- 关注事件的后续发展和潜在催化剂
- 市场反应可能存在过度或不足，需理性判断
- 如工具调用失败或无新闻数据，应明确说明无法进行事件分析";

    private static IList<AITool> CreateTools(StockNewsTools newsTools)
    {
        var tools = new List<AITool>();
        tools.AddRange(newsTools.GetFunctions());
        return tools;
    }
}
