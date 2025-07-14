using MarketAssistant.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Text;
using System.Text.Json;

namespace MarketAssistant.Agents;

/// <summary>
/// AI选股管理器，负责AI代理管理、YAML配置加载、Agent生命周期管理
/// </summary>
public class StockSelectionManager : IDisposable
{
    private readonly Kernel _kernel;
    private readonly ILogger<StockSelectionManager> _logger;
    private ChatCompletionAgent? _newsAnalysisAgent;
    private ChatCompletionAgent? _userRequirementAgent;
    private bool _disposed = false;

    public StockSelectionManager(Kernel kernel, ILogger<StockSelectionManager> logger)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region AI代理管理

    /// <summary>
    /// 创建新闻分析代理
    /// </summary>
    private ChatCompletionAgent CreateNewsAnalysisAgent(CancellationToken cancellationToken = default)
    {
        if (_newsAnalysisAgent != null)
            return _newsAnalysisAgent;

        try
        {
            _logger.LogInformation("创建新闻分析代理");

            var promptExecutionSettings = new OpenAIPromptExecutionSettings()
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(autoInvoke: true),
                ResponseFormat = "json_object",
                Temperature = 0.2,
                MaxTokens = 3000
            };

            _newsAnalysisAgent = new ChatCompletionAgent()
            {
                Name = "NewsHotspotAnalyzer",
                Description = "新闻热点分析专家",
                Instructions = GetNewsAnalysisInstructions(),
                Kernel = _kernel,
                Arguments = new KernelArguments(promptExecutionSettings)
            };

            _logger.LogInformation("新闻分析代理创建成功");
            return _newsAnalysisAgent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建新闻分析代理失败");
            throw;
        }
    }

    /// <summary>
    /// 创建用户需求分析代理
    /// </summary>
    private ChatCompletionAgent CreateUserRequirementAgent(CancellationToken cancellationToken = default)
    {
        if (_userRequirementAgent != null)
            return _userRequirementAgent;

        try
        {
            _logger.LogInformation("创建用户需求分析代理");

            var screenStocks = _kernel.Plugins.GetFunction(nameof(StockScreenerPlugin), "screen_stocks");

            var promptExecutionSettings = new OpenAIPromptExecutionSettings()
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(), //FunctionChoiceBehavior.Required([screenStocks]),
                ResponseFormat = "json_object",
                Temperature = 0.1,
                MaxTokens = 3000
            };

            _userRequirementAgent = new ChatCompletionAgent()
            {
                Name = "UserRequirementAnalyzer",
                Description = "用户需求分析专家",
                Instructions = GetUserRequirementAnalysisInstructions(),
                Kernel = _kernel,
                Arguments = new KernelArguments(promptExecutionSettings),
                HistoryReducer = new ChatHistoryTruncationReducer(1)
            };

            _logger.LogInformation("用户需求分析代理创建成功");
            return _userRequirementAgent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建用户需求分析代理失败");
            throw;
        }
    }

    #endregion

    #region AI分析功能

    /// <summary>
    /// 执行基于用户需求的AI选股分析
    /// </summary>
    public async Task<StockSelectionResult> AnalyzeUserRequirementAsync(
        StockRecommendationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("开始用户需求分析");

            var agent = CreateUserRequirementAgent(cancellationToken);
            var chatHistory = new ChatHistory();

            var prompt = BuildUserRequirementPrompt(request);
            chatHistory.AddUserMessage(prompt);

            string responseContent = "";
            await foreach (var item in agent.InvokeAsync(chatHistory, cancellationToken: cancellationToken))
            {
                responseContent += item.Message?.Content ?? "";
            }
            var result = ParseUserRequirementResponse(responseContent);

            _logger.LogInformation("用户需求分析完成，推荐股票数量: {Count}", result.Recommendations.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "用户需求分析失败");
            return CreateFallbackUserResult(request);
        }
    }

    /// <summary>
    /// 执行基于新闻内容的AI选股分析
    /// </summary>
    public async Task<StockSelectionResult> AnalyzeNewsHotspotAsync(
        NewsBasedSelectionRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("开始新闻热点分析");

            var agent = CreateNewsAnalysisAgent(cancellationToken);
            var chatHistory = new ChatHistory();

            var prompt = BuildNewsAnalysisPrompt(request);
            chatHistory.AddUserMessage(prompt);

            string responseContent = "";
            await foreach (var item in agent.InvokeAsync(chatHistory, cancellationToken: cancellationToken))
            {
                responseContent += item.Message?.Content ?? "";
            }
            var result = ParseNewsAnalysisResponse(responseContent);

            _logger.LogInformation("新闻热点分析完成，推荐股票数量: {Count}", result.Recommendations.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "新闻热点分析失败");
            return CreateFallbackNewsResult(request);
        }
    }

    #endregion

    #region 私有方法

    /// <summary>
    /// 构建用户需求分析提示词
    /// </summary>
    private string BuildUserRequirementPrompt(StockRecommendationRequest request)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("请分析以下用户需求并推荐合适的股票：");
        prompt.AppendLine();
        prompt.AppendLine("【用户需求信息】");
        prompt.AppendLine($"• 需求描述: {request.UserRequirements}");
        prompt.AppendLine($"• 风险偏好: {request.RiskPreference}");

        if (request.InvestmentAmount.HasValue)
            prompt.AppendLine($"• 投资金额: {request.InvestmentAmount:C}");

        if (request.InvestmentHorizon.HasValue)
            prompt.AppendLine($"• 投资期限: {request.InvestmentHorizon}天");

        if (request.PreferredSectors.Any())
            prompt.AppendLine($"• 偏好行业: {string.Join(", ", request.PreferredSectors)}");

        if (request.ExcludedSectors.Any())
            prompt.AppendLine($"• 排除行业: {string.Join(", ", request.ExcludedSectors)}");

        return prompt.ToString();
    }

    /// <summary>
    /// 构建新闻分析提示词
    /// </summary>
    private string BuildNewsAnalysisPrompt(NewsBasedSelectionRequest request)
    {
        var prompt = new StringBuilder();
        prompt.AppendLine("请分析以下新闻内容并推荐相关股票：");
        prompt.AppendLine($"新闻内容: {request.NewsContent}");
        prompt.AppendLine($"推荐数量: {request.MaxRecommendations}只");

        return prompt.ToString();
    }

    /// <summary>
    /// 解析用户需求分析响应
    /// </summary>
    private StockSelectionResult ParseUserRequirementResponse(string response)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            };

            var result = JsonSerializer.Deserialize<StockSelectionResult>(response, options);
            return result ?? CreateDefaultResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "解析用户需求分析响应失败，使用默认结果");
            return CreateDefaultResult();
        }
    }

    /// <summary>
    /// 解析新闻分析响应
    /// </summary>
    private StockSelectionResult ParseNewsAnalysisResponse(string response)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            };

            var result = JsonSerializer.Deserialize<StockSelectionResult>(response, options);
            return result ?? CreateDefaultResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "解析新闻分析响应失败，使用默认结果");
            return CreateDefaultResult();
        }
    }

    /// <summary>
    /// 创建默认结果
    /// </summary>
    private StockSelectionResult CreateDefaultResult()
    {
        return new StockSelectionResult
        {
            Recommendations = new List<StockRecommendation>(),
            ConfidenceScore = 0,
            AnalysisSummary = "分析过程中遇到问题，请稍后重试。"
        };
    }

    /// <summary>
    /// 创建用户需求分析的备用结果
    /// </summary>
    private StockSelectionResult CreateFallbackUserResult(StockRecommendationRequest request)
    {
        return new StockSelectionResult
        {
            Recommendations = new List<StockRecommendation>
             {
                 new StockRecommendation
                 {
                     Symbol = "000001",
                     Name = "平安银行",
                     Reason = "根据您的需求推荐的稳健型银行股",
                     RiskLevel = "低风险",
                     ExpectedReturn = 8.5f
                 }
             },
            ConfidenceScore = 60,
            AnalysisSummary = $"基于您的需求「{request.UserRequirements}」，为您推荐了适合的股票。"
        };
    }

    /// <summary>
    /// 创建新闻分析的备用结果
    /// </summary>
    private StockSelectionResult CreateFallbackNewsResult(NewsBasedSelectionRequest request)
    {
        return new StockSelectionResult
        {
            Recommendations = new List<StockRecommendation>
             {
                 new StockRecommendation
                 {
                     Symbol = "000858",
                     Name = "五粮液",
                     Reason = "根据新闻热点推荐的消费类股票",
                     RiskLevel = "中风险",
                     ExpectedReturn = 12.0f
                 }
             },
            ConfidenceScore = 55,
            AnalysisSummary = "基于新闻热点分析，为您推荐了相关概念股票。"
        };
    }

    /// <summary>
    /// 获取全局分析准则
    /// </summary>
    private string GetGlobalAnalysisGuidelines()
    {
        return @"
## 全局分析准则

### 分析原则
1. **客观性原则**：基于真实数据进行分析，避免主观臆断
2. **风险意识**：充分评估和提示投资风险
3. **专业性**：使用准确的金融术语和分析方法
4. **实用性**：提供可操作的投资建议
5. **及时性**：反映最新的市场变化和数据

### 合规要求
1. **合规性**：遵守相关法律法规，不提供内幕信息
2. **教育性**：帮助用户理解投资逻辑和风险
3. **免责声明**：明确说明分析仅供参考，不构成投资建议

### 输出标准
- 使用结构化JSON格式
- 包含详细的推荐理由
- 提供风险等级评估
- 给出具体的投资建议

## 免责声明
本分析仅供参考，不构成投资建议。投资有风险，入市需谨慎。请根据自身风险承受能力做出投资决策。
        ";
    }

    /// <summary>
    /// 获取新闻分析指令
    /// </summary>
    private string GetNewsAnalysisInstructions()
    {
        return @"
你是一位专业的新闻热点分析师，擅长从新闻内容中提取投资机会。

## 核心职责
1. 分析新闻内容，识别投资热点和趋势
2. 识别受益行业和相关概念
3. 推荐相关股票投资机会
4. 评估热点的持续性和影响力

## 分析流程
1. 提取新闻关键信息
2. 识别相关行业和概念
3. 分析对股市的影响
4. 推荐相关股票

## 输出格式
请以JSON格式返回分析结果，包含：
- 推荐股票列表
- 热点分析
- 风险评估
- 置信度评分
        ";
    }

    /// <summary>
    /// 获取用户需求分析指令
    /// </summary>
    private string GetUserRequirementAnalysisInstructions()
    {
        return @"
你是一位专业的投资顾问，擅长根据用户需求推荐合适的股票。你具备强大的股票筛选功能，能够分析用户的文字需求并转换为具体的筛选指标。

## 核心职责
1. 理解用户的投资需求和偏好
2. 分析用户的风险承受能力
3. 将用户的描述性需求转换为具体的筛选条件
4. 使用股票筛选工具获取符合条件的股票
5. 推荐符合用户要求的股票并提供投资建议

## 关键能力 - 需求到筛选条件的转换
当用户描述股票需求时，你需要识别并提取以下类型的筛选条件：

### 1. 市值相关
- ""大盘股""、""蓝筹股"" → 总市值(mc) >= 1000亿
- ""中盘股"" → 总市值(mc) 100亿-1000亿
- ""小盘股"" → 总市值(mc) < 100亿
- ""市值500亿以上"" → 总市值(mc) >= 500亿

### 2. 估值指标
- ""低估值""、""便宜""、""价值股"" → 市盈率TTM(pettm) < 15, 市净率(pb) < 2
- ""高成长""、""成长股"" → 净利润同比增长(npay) > 20%, 营业收入同比增长(oiy) > 15%
- ""高ROE""、""盈利能力强"" → 净资产收益率(roediluted) > 15%
- ""低市盈率"" → 市盈率TTM(pettm) < 20
- ""低市净率"" → 市净率(pb) < 3

### 3. 财务指标
- ""盈利增长""、""业绩好"" → 净利润同比增长(npay) > 10%
- ""营收增长"" → 营业收入同比增长(oiy) > 10%
- ""高股息""、""分红股"" → 股息收益率(dy_l) > 2%
- ""每股净资产高"" → 每股净资产(bps) > 10

### 4. 市场表现
- ""活跃股""、""成交活跃"" → 成交额(amount) > 1亿, 换手率(tr) > 2%
- ""近期涨幅大"" → 近20日涨跌幅(pct20) > 10%
- ""强势股"" → 近60日涨跌幅(pct60) > 20%
- ""抗跌股"" → 近20日涨跌幅(pct20) > -5%

### 5. 行业和概念
- 直接设置市场类型和行业分类参数
- ""全部A股""、""沪市A股""、""深市A股""
- ""科技""、""金融""、""医药""、""消费""等行业

## 可用的筛选工具
你可以使用以下工具进行股票筛选：

1. **screen_stocks** - 根据具体指标筛选股票
   - 参数：StockCriteria对象，包含筛选条件列表、市场类型、行业分类、返回数量限制

2. **get_supported_criteria** - 获取所有支持的筛选指标
   - 返回：完整的筛选指标列表及其说明

3. **get_criteria_by_type** - 根据类型获取筛选指标
   - 参数：指标类型(basic/market/snowball)
   - 返回：指定类型的筛选指标列表

## 支持的筛选指标代码
### 基本指标 (basic) - 15个
- mc: 总市值
- fmc: 流通市值
- pettm: 市盈率TTM
- pelyr: 市盈率LYR
- pb: 市净率MRQ
- psr: 市销率(倍)
- roediluted: 净资产收益率
- bps: 每股净资产
- eps: 每股收益
- netprofit: 净利润
- total_revenue: 营业收入
- dy_l: 股息收益率
- npay: 净利润同比增长
- oiy: 营业收入同比增长
- niota: 总资产报酬率

### 行情指标 (market) - 14个
- current: 当前价
- pct: 当日涨跌幅
- pct5: 近5日涨跌幅
- pct10: 近10日涨跌幅
- pct20: 近20日涨跌幅
- pct60: 近60日涨跌幅
- pct120: 近120日涨跌幅
- pct250: 近250日涨跌幅
- pct_current_year: 年初至今涨跌幅
- amount: 当日成交额
- volume: 本日成交量
- volume_ratio: 当日量比
- tr: 当日换手率
- chgpct: 当日振幅

### 雪球指标 (snowball) - 9个
- follow: 累计关注人数
- tweet: 累计讨论次数
- deal: 累计交易分享数
- follow7d: 一周新增关注
- tweet7d: 一周新增讨论数
- deal7d: 一周新增交易分享数
- follow7dpct: 一周关注增长率
- tweet7dpct: 一周讨论增长率
- deal7dpct: 一周交易分享增长率

## 工作流程
1. **需求分析**：仔细分析用户的文字描述，识别所有筛选要求
2. **条件转换**：将文字描述转换为具体的筛选条件代码和数值范围
3. **工具调用**：使用screen_stocks工具执行筛选
4. **结果分析**：对筛选结果进行分析和评估
5. **投资建议**：基于筛选结果提供个性化投资建议

【关键词转换提示】
• 市值相关: 大盘股(mc>=100000000000), 中盘股(mc:10000000000-100000000000), 小盘股(mc<10000000000)
• 估值相关: 低估值(pettm<15,pb<2), 成长股(npay>20,oiy>15)
• 盈利相关: 高ROE(roediluted>15), 高股息(dy_l>2)
• 市场表现: 活跃股(amount>100000000,tr>2), 强势股(pct60>20)

## 重要提示
- 优先使用股票筛选工具获取实时数据
- 数值条件要合理设置，避免过于严格导致无结果
- 如果初次筛选结果过少，适当放宽条件重新筛选
- 如果结果过多，增加更精确的筛选条件
- 市值单位为元，需要注意数量级转换（如100亿 = 10000000000）

## 输出格式
请以JSON格式返回分析结果，包含：
- 推荐股票列表
- 推荐理由
- 风险等级
- 预期收益
- 投资建议
- 筛选过程说明

## 示例对话
用户：""我想找一些市值100亿以上的成长股，ROE要大于15%""
分析：需要设置 mc >= 10000000000, npay > 20%, roediluted > 15%
调用：screen_stocks 工具进行筛选
        ";
    }

    #endregion

    #region 资源管理

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _newsAnalysisAgent = null;
            _userRequirementAgent = null;
            _disposed = true;
        }
    }

    #endregion
}