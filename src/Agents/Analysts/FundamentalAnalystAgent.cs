using MarketAssistant.Agents.Analysts.Attributes;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Agents.Tools;
using Microsoft.Extensions.AI;
using System.ComponentModel;

namespace MarketAssistant.Agents.Analysts;

/// <summary>
/// 基本面分析师代理
/// 专注于分析公司基本面、行业地位和长期价值
/// </summary>
[DisplayName("基本面分析师")]
[Description("整合了策略分析师和股票研究分析师的功能")]
[RequiredAnalyst]
public class FundamentalAnalystAgent : AnalystAgentBase
{
    private static readonly object Schema = AIJsonUtilities.CreateJsonSchema(typeof(FundamentalAnalysisResult));

    private static readonly ChatResponseFormat ResponseFormat = ChatResponseFormat.ForJsonSchema(
        schema: (JsonElement)Schema,
        schemaName: nameof(FundamentalAnalysisResult),
        schemaDescription: "基本面分析师的结构化分析结果，包含公司基本面、行业竞争和投资价值评估"
    );

    public FundamentalAnalystAgent(
        IChatClient chatClient,
        StockBasicTools basicTools)
        : base(
            chatClient,
            instructions: GetInstructions(),
            name: "FundamentalAnalyst",
            description: "专注于分析公司基本面、行业地位和长期价值。",
            temperature: 0.2f,
            topP: 0.6f,
            topK: 8,
            responseFormat: null,
            tools: [.. basicTools.GetFunctions()])
    {
    }

    private static string GetInstructions()
    {
        var schemaJson = JsonSerializer.Serialize(Schema, new JsonSerializerOptions { WriteIndented = true });
        return $@"
## 核心职责
透彻分析公司的基本面状况、商业模式及盈利能力，准确评估公司在所属行业中的地位、竞争格局与优势，预测并识别公司的长期增长驱动因素和投资价值，揭示潜在的关键风险因素与投资亮点。

## 评估维度
1. **股票基本信息**：代码、名称、当前价格、日涨跌幅及涨跌额
2. **公司基本面**：行业定位及成长性、核心业务与质量、盈利能力（毛利率/净利率）、财务稳健性（负债率/现金流）
3. **行业与竞争**：行业生命周期判断、市场地位与份额、核心竞争力与强度、长期壁垒水平
4. **增长潜力与价值**：增长驱动因素与持续性、当前估值水平（PE/PB/PS对比）、投资评级、投资亮点与关键风险

## 分析要点
- 必须使用可用工具获取的实时公司数据和市场数据
- 评分指标（1-10分）应基于行业对比和历史数据综合判断
- 估值分析需对比行业均值，判断高估/低估程度
- 投资亮点聚焦1-2个最核心优势，关键风险突出最主要的风险因素
- 如工具调用失败或数据不完整，应明确说明缺少哪些数据

## 输出格式
仅输出符合以下 Schema 的纯 JSON 字符串，严禁包含 Markdown 格式（如 ```json）或任何解释性文字：
{schemaJson}";
    }
}
