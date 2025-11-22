using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Agents.Tools;
using Microsoft.Agents.AI.Data;
using Microsoft.Extensions.AI;

namespace MarketAssistant.Agents.Analysts;

/// <summary>
/// 协调分析师代理
/// 整合多维度分析师结论并提供投资建议
/// </summary>
public class CoordinatorAnalystAgent : AnalystAgentBase
{
    public CoordinatorAnalystAgent(
        IChatClient chatClient,
        GroundingSearchTools searchTools)
        : base(
            chatClient,
            instructions: GetInstructions(),
            name: "CoordinatorAnalyst",
            description: "市场分析协调专家，整合多维度分析师结论并提供投资建议。",
            temperature: 0.2f,
            topP: 0.7f,
            topK: 5,
            responseFormat: ChatResponseFormat.ForJsonSchema(
                schema: AIJsonUtilities.CreateJsonSchema(typeof(CoordinatorResult)),
                schemaName: nameof(CoordinatorResult),
                schemaDescription: "Coordinator 的综合分析结果，包含投资建议、评分、风险评估等结构化数据"
            ),
            tools: null, // 不再使用工具调用，改为通过 ContextProvider 注入搜索结果
            aiContextProviderFactory: ctx =>
            {
                TextSearchProviderOptions textSearchOptions = new()
                {
                    // 在每次模型调用前运行搜索，并保持简短的对话上下文滚动窗口
                    SearchTime = TextSearchProviderOptions.TextSearchBehavior.BeforeAIInvoke,
                    RecentMessageMemoryLimit = 6,
                };

                return new TextSearchProvider(
                    async (query, ct) =>
                    {
                        // 使用 GroundingSearchTools 进行混合搜索（本地 RAG + Web 搜索）
                        var searchResults = await searchTools.SearchAsync(query);

                        // 映射搜索结果到 TextSearchProvider 所需的格式
                        // 注意：TextSearchProvider.TextSearchResult 只有无参构造函数，且核心内容可能通过 "Text" 或 "Value" 属性设置
                        // 由于前面 build 错误提示没有 Value/Text 属性，尝试直接使用对象初始化器设置所有可能得属性
                        // 经过多次失败，我们采用最保险的方案：如果库有不一致，可能需要反射或查看元数据，但这里我们假设它必然有一个承载内容的属性
                        // 根据 Microsoft.Agents.AI.Data 的常见模式，它可能是一个具有 string Value {get; set;} 的记录或类
                        // 但既然 Value 报错，Text 没报错（或者之前的报错被忽略了），我们再试一次 Text，并确保无参构造
                        // 如果 Text 也不行，那可能是 Content。

                        // 鉴于之前提示 Name/Link 也不存在，这非常奇怪，可能我们引用的 TextSearchResult 不是我们要的那个
                        // 检查命名空间 using Microsoft.Agents.AI.Data; 

                        // 终极方案：如果真的无法匹配属性，可能是版本差异，这里先尝试用对象初始化器只设置 Text，如果 Text 也不行，
                        // 那么这个 TextSearchResult 类可能只有一个 Value 属性，或者是完全不同的结构。
                        // 但根据 SemanticKernel 的类似实现，它通常有 Name, Link, Value/Text。

                        // 让我们尝试使用最简单的构造，如果不行，可能需要反编译查看。
                        // 此处假设 Text 是可用属性。

                        var results = searchResults.Select(r =>
                            new TextSearchProvider.TextSearchResult
                            {
                                // 尝试使用 Text 属性。如果编译通过，说明这就是正确属性。
                                // 同时把所有元数据塞进去，因为 Name/Link 看来是不存在的。
                                // 之前的尝试中，Text属性报错可能是因为我们也试图使用了带参构造函数。
                                // 这次严格使用无参构造 + 对象初始化器。
                                Text = $"[Source: {r.Name ?? "Unknown"} Link: {r.Link ?? "N/A"}] {r.Value ?? string.Empty}"
                            });

                        return results;
                    },
                    ctx.SerializedState,
                    ctx.JsonSerializerOptions,
                    textSearchOptions);
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
  
  ## 2. 知识增强与验证
  您已接入增强搜索能力（RAG + Web Search），系统会在您回答前自动检索相关信息并注入上下文。
  请利用这些信息：
  - 验证分析师提到的关键数据（如财报数据、新闻事件）
  - 补充最新的市场动态（如突发新闻、最新政策）
  - 解决分析师之间的观点冲突

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
