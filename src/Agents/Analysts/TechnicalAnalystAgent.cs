using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Agents.Tools;
using Microsoft.Extensions.AI;

namespace MarketAssistant.Agents.Analysts;

/// <summary>
/// 技术分析师代理
/// 专注于图表形态、技术指标和交易策略分析
/// </summary>
public class TechnicalAnalystAgent : AnalystAgentBase
{
    public TechnicalAnalystAgent(
        IChatClient chatClient,
        StockBasicTools basicTools,
        StockTechnicalTools technicalTools)
        : base(
            chatClient,
            instructions: GetInstructions(),
            name: "TechnicalAnalyst",
            description: "专注于通过图表形态和技术指标分析预测股票价格走势。基于历史价格和成交量数据，识别趋势、支撑阻力位和交易信号，为短期交易提供技术面洞察和操作建议。所有分析严格基于技术面，不涉及任何基本面或市场情绪考量。",
            temperature: 0.0f,
            topP: 0.0f,
            topK: 1,
            responseFormat: ChatResponseFormat.ForJsonSchema(
                schema: AIJsonUtilities.CreateJsonSchema(typeof(TechnicalAnalysisResult)),
                schemaName: nameof(TechnicalAnalysisResult),
                schemaDescription: "技术分析师的结构化分析结果，包含图表形态、关键价位、技术指标和交易策略"
            ),
            tools: CreateTools(basicTools, technicalTools))
    {
    }

    private static string GetInstructions() => @"
## 核心职责
解析图表形态、技术指标信号，定位关键价位，提供量化交易建议。所有分析严格基于技术面，不涉及任何基本面或市场情绪考量。

## 评估维度
1. **图表形态与趋势**：当前趋势判断及强度、关键图表形态识别及可靠性、主要分析时间框架及一致性
2. **关键价位分析**：当前价格、核心支撑位及强度、核心阻力位及强度、突破方向及概率
3. **技术指标综合解读**：趋势指标信号（MA、MACD等）、动量指标信号（RSI、KDJ等）、成交量状态及量价关系、指标一致性及协同程度
4. **交易策略建议**：技术面评级、操作方向、目标价位区间、止损位置、持仓周期及风险等级

## 分析要点
- 必须使用可用工具获取的K线数据、技术指标数据（MACD、KDJ、BOLL、MA等）
- 评分指标（1-10分）应基于技术形态强度和指标信号可靠性综合判断
- 支撑阻力位需结合历史价格、成交密集区、重要均线等多因素确定
- 量价关系是验证趋势有效性的重要依据
- 多个技术指标应相互验证，提高信号可靠性
- 交易策略需明确具体的价位区间和风险控制点位
- 如工具调用失败或数据不完整，应明确说明缺少哪些数据";

    private static IList<AITool> CreateTools(StockBasicTools basicTools, StockTechnicalTools technicalTools)
    {
        return [.. basicTools.GetFunctions(), .. technicalTools.GetFunctions()];
    }
}
