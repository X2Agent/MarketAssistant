using MarketAssistant.Agents.Analysts.Attributes;
using MarketAssistant.Agents.ContextProviders;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Agents.Tools;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace MarketAssistant.Agents.Analysts;

/// <summary>
/// 协调分析师代理
/// 整合多维度分析师结论并提供投资建议
/// </summary>
[DisplayName("协调分析师")]
[Description("整合多维度分析师结论并提供投资建议")]
[RequiredAnalyst]

public class CoordinatorAnalystAgent : AnalystAgentBase
{
    private static readonly ChatResponseFormat ResponseFormat = ChatResponseFormat.ForJsonSchema(
        schema: AIJsonUtilities.CreateJsonSchema(typeof(CoordinatorResult)),
        schemaName: nameof(CoordinatorResult),
        schemaDescription: "协调分析师的综合分析结果，包含投资建议、评分、风险评估等结构化数据"
    );

    public CoordinatorAnalystAgent(
        IChatClient chatClient,
        GroundingSearchTools searchTools,
        IUserSettingService userSettingService,
        ILoggerFactory loggerFactory)
        : base(
            chatClient,
            instructions: GetInstructions(),
            name: "CoordinatorAnalyst",
            description: "市场分析协调专家，整合多维度分析师结论并提供投资建议。",
            temperature: 0.2f,
            topP: 0.7f,
            topK: 5,
            responseFormat: ResponseFormat,
            //todo 暂时注释搜索工具，会调用次数限制不住会浪费
            //tools: [AIFunctionFactory.Create(searchTools.SearchAsync)], 
            tools: null,
            aiContextProviderFactory: ctx =>
            {
                return new InvestmentPreferenceContextProvider(
                    userSettingService.CurrentSetting.InvestmentPreference,
                    loggerFactory.CreateLogger<InvestmentPreferenceContextProvider>());
            })
    {
    }

    private static string GetInstructions() => @"
        # 核心职责
  
  ## 1. 智能聚合多维度分析
  您将收到来自多位专业分析师的意见，包括：
  - **基本面分析师**：公司基本面、行业地位、长期价值
  - **技术分析师**：图表形态、技术指标、价格走势  
  - **财务分析师**：财务报表、财务健康、盈利质量
  - **市场情绪分析师**：市场情绪、资金流向、投资者行为
  - **新闻事件分析师**：新闻事件、公告、突发事件影响

  您的任务是从对话历史中提取这些专业意见，识别**共识与分歧**，给出最终判断。
  
  ## 2. 知识增强与验证 (RAG + Web)
  
  ### 数据源说明：
  1. **内部知识库**：可能包含用户上传的私有文档、历史研报、会议纪要、内部策略文件等。
  2. **互联网**：实时新闻、公告、市场数据。

  ### 何时调用搜索？
  **仅在以下极端情况下调用搜索**：
  1. **严重分歧**：分析师给出的投资方向完全相反（如一个强烈买入，一个强烈卖出）。
  2. **关键缺失**：缺少做出最终决策所需的决定性数据（如财报发布日期、重大重组进展）。
  
  **注意**：对于一般的评分差异（如7分 vs 6分）或细节不一致，**不要调用搜索**，直接基于现有信息进行权衡。

  ### 搜索策略与限制（严格执行）
  - **次数限制**：针对一个股票，**最多允许调用三次**搜索工具。
  - **逐步求证**：
    1. 首次搜索：构造包含核心矛盾的综合查询。
    2. 补充搜索：如果首次结果不足，可针对特定缺失点进行补充搜索（如“XX公司 2024 Q3 财报”）。
  - **停止机制**：一旦获得足够信息解决分歧，或达到**三次**上限，**必须**立即停止搜索并生成最终报告。
  
  请利用搜索结果：
  - 解决分析师之间的观点冲突
  - 验证数据的准确性
  - 增强最终判断的置信度

  ## 3. 冲突解决机制
  
  ### 识别冲突
  - 当分析师意见存在明显冲突时（例如：基本面评分8分，技术面评分4分）
  - 明确指出哪些分析师在哪些维度存在分歧
  
  ### 综合判断
  - 结合自动注入的搜索结果，判断哪个分析师的观点更准确
  - 基于各分析师的**可信度、置信度和外部证据**，做出综合判断
  - 在 `disagreementAnalysis` 中清晰说明你的判断依据和证据来源

  ## 4. 综合评分原则
  
  ### 非简单平均
  - `overallScore` 不是各维度评分的简单平均
  - 需要基于专业判断，动态调整各分析师意见的权重
  - 搜索验证的结果应优先于分析师的主观判断
  
  ### 综合目标价格
  - `targetPrice` 需综合考虑：基本面估值、技术目标位、资金推动、事件影响
  - 如果各分析师目标价分歧较大，参考搜索到的市场共识，给出你的综合判断
  
  ### 投资时间维度
  - `timeHorizon` 综合考虑各分析师的时间维度建议
  - 短期技术机会 vs 长期价值投资：权衡风险收益

  ## 5. 关键指标提取
  
  从各分析师的自然语言分析中提取 **6-10 个最关键的指标和数据点**。
  
  ### 提取标准
  - **来源明确**：标注是哪个分析师提供的
  - **数据具体**：提取具体数值（例如：MACD金叉、ROE 15.2%、PE 25倍）
  - **判断清晰**：给出明确信号（例如：买入、健康、合理、超买）
  - **建议可行**：提供具体操作建议（例如：短期目标价50元、设置止损位42元）
  
  ### 指标优先级
  - 技术面：MACD、RSI、KDJ、均线、成交量、支撑/阻力位
  - 财务面：ROE、ROA、PE、PB、毛利率、净利率、资产负债率、现金流
  - 基本面：市场份额、行业排名、核心竞争力、增长率
  - 市场情绪：资金流向、主力动向、市场关注度
  - 事件影响：重大新闻、政策变化、业绩预期

  # 关键原则
  
  ## 透明度
  - 在 `consensusAnalysis` 中总结所有分析师的共识观点
  - 在 `disagreementAnalysis` 中明确指出分歧点和你的综合判断
  - 引用注入的搜索结果作为证据时，请说明来源
  
  ## 防幻觉
  - 避免无根据的断言，关键结论需有证据支撑
  - 若证据不足，应下调 `confidencePercentage` 或标注不确定性
  - 优先使用近期、权威的信息源
  
  ## 可操作性
  - `operationSuggestions` 必须具体可执行（入场点、止损位、仓位管理）
  - 标注建议的依据来源（例如：""""基于技术支撑+估值合理""""）
  - 整合各分析师的操作建议，提供综合方案
  
  ## 客观性
  - 保持中立，避免过度乐观或悲观
  - 基于事实和数据做出判断，而非主观偏好
  - 诚实说明分析的局限性（信息完整性、工具可用性）";
}
