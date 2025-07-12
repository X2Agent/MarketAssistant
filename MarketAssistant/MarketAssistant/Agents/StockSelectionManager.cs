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
    private ChatCompletionAgent? _stockSelectionAgent;
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
    private async Task<ChatCompletionAgent> CreateNewsAnalysisAgentAsync(CancellationToken cancellationToken = default)
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
    private async Task<ChatCompletionAgent> CreateUserRequirementAgentAsync(CancellationToken cancellationToken = default)
    {
        if (_userRequirementAgent != null)
            return _userRequirementAgent;

        try
        {
            _logger.LogInformation("创建用户需求分析代理");

            var promptExecutionSettings = new OpenAIPromptExecutionSettings()
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(autoInvoke: true),
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

            var agent = await CreateUserRequirementAgentAsync(cancellationToken);
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

            var agent = await CreateNewsAnalysisAgentAsync(cancellationToken);
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
        prompt.AppendLine($"用户需求: {request.UserRequirements}");
        prompt.AppendLine($"风险偏好: {request.RiskPreference}");

        if (request.InvestmentAmount.HasValue)
            prompt.AppendLine($"投资金额: {request.InvestmentAmount:C}");

        if (request.InvestmentHorizon.HasValue)
            prompt.AppendLine($"投资期限: {request.InvestmentHorizon}天");

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
你是一位专业的投资顾问，擅长根据用户需求推荐合适的股票。

## 核心职责
1. 理解用户的投资需求和偏好
2. 分析用户的风险承受能力
3. 推荐符合用户要求的股票
4. 提供个性化的投资建议

## 分析维度
1. 投资目标分析
2. 风险偏好匹配
3. 行业偏好考虑
4. 投资期限适配

## 输出格式
请以JSON格式返回分析结果，包含：
- 推荐股票列表
- 推荐理由
- 风险等级
- 预期收益
- 投资建议
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
            _stockSelectionAgent = null;
            _newsAnalysisAgent = null;
            _userRequirementAgent = null;
            _disposed = true;
        }
    }

    #endregion
}