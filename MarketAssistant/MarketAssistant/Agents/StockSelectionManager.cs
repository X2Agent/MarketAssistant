using MarketAssistant.Infrastructure;
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
/// AI选股管理器，负责AI代理管理和Agent生命周期管理
/// </summary>
public class StockSelectionManager : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IKernelFactory _kernelFactory;
    private Kernel? _kernel; // 实际使用的克隆 Kernel（注册本 Manager 所需插件）
    private readonly ILogger<StockSelectionManager> _logger;
    private ChatCompletionAgent? _newsAnalysisAgent;
    private ChatCompletionAgent? _userRequirementAgent;
    private bool _disposed = false;

    // YAML 模板文件名称常量
    private const string UserRequirementYaml = "user_requirement_to_stock_criteria.yaml";
    private const string NewsAnalysisYaml = "news_analysis_to_stock_criteria.yaml";

    // 统一的 JSON 反序列化配置，避免重复创建
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public StockSelectionManager(
    IServiceProvider serviceProvider,
    IKernelFactory kernelFactory,
        ILogger<StockSelectionManager> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _kernelFactory = kernelFactory ?? throw new ArgumentNullException(nameof(kernelFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region AI代理管理

    /// <summary>
    /// 创建新闻分析代理
    /// </summary>
    private ChatCompletionAgent CreateNewsAnalysisAgent(string? criteriaJson = null, CancellationToken cancellationToken = default)
    {
        if (_newsAnalysisAgent != null && string.IsNullOrEmpty(criteriaJson))
            return _newsAnalysisAgent;

        try
        {
            _logger.LogInformation("创建新闻分析代理");
            if (!TryEnsureKernel(out var error))
                throw new InvalidOperationException($"Kernel 未就绪：{error}");

            _newsAnalysisAgent = CreateOrUpdateAgent(
                existing: _newsAnalysisAgent,
                name: "NewsHotspotAnalyzer",
                description: "新闻热点分析专家",
                instructions: GetNewsAnalysisInstructions(),
                maxTokens: 3000,
                temperature: 0.2,
                criteriaJson: criteriaJson);

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
    private ChatCompletionAgent CreateUserRequirementAgent(string? criteriaJson = null, CancellationToken cancellationToken = default)
    {
        if (_userRequirementAgent != null)
            return _userRequirementAgent;
        try
        {
            _logger.LogInformation("创建用户需求分析代理");
            if (!TryEnsureKernel(out var error))
                throw new InvalidOperationException($"Kernel 未就绪：{error}");

            _userRequirementAgent = CreateOrUpdateAgent(
                existing: _userRequirementAgent,
                name: "UserRequirementAnalyzer",
                description: "用户需求分析专家",
                instructions: GetUserRequirementAnalysisInstructions(),
                maxTokens: 8000,
                temperature: 0.2,
                criteriaJson: criteriaJson);
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
            if (!TryEnsureKernel(out var kernelError))
            {
                _logger.LogWarning("用户需求分析提前终止：Kernel 未就绪 {Error}", kernelError);
                return CreateDefaultResult(problem: kernelError);
            }

            string criteriaJson = await BuildCriteriaJsonAsync(
                yamlFileName: UserRequirementYaml,
                argumentBuilder: args =>
                {
                    args["user_requirements"] = request.UserRequirements;
                    args["limit"] = request.MaxRecommendations;
                },
                maxTokens: 2000,
                cancellationToken: cancellationToken);

            // 第二步：使用筛选条件调用股票筛选插件并进行分析
            var chatHistory = new ChatHistory();
            var prompt = BuildUserRequirementPrompt(request);

            chatHistory.AddUserMessage(prompt);

            var agent = CreateUserRequirementAgent(criteriaJson, cancellationToken);

            var responseContent = await InvokeAgentAndAggregateAsync(agent, chatHistory, cancellationToken);
            var result = ParseResponse(responseContent);

            _logger.LogInformation("用户需求分析完成，推荐股票数量: {Count}", result.Recommendations.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "用户需求分析失败");
            // 返回带有错误信息的默认结果，供UI展示
            return CreateDefaultResult(ex.Message);
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
            if (!TryEnsureKernel(out var kernelError))
            {
                _logger.LogWarning("新闻热点分析提前终止：Kernel 未就绪 {Error}", kernelError);
                return CreateDefaultResult(problem: kernelError);
            }

            string criteriaJson = await BuildCriteriaJsonAsync(
                yamlFileName: NewsAnalysisYaml,
                argumentBuilder: args =>
                {
                    args["news_content"] = request.NewsContent;
                    args["limit"] = request.MaxRecommendations;
                },
                maxTokens: 2500,
                cancellationToken: cancellationToken);

            // 第二步：使用筛选条件调用股票筛选插件并进行分析
            var chatHistory = new ChatHistory();
            var prompt = BuildNewsAnalysisPrompt(request);
            chatHistory.AddUserMessage(prompt);

            var agent = CreateNewsAnalysisAgent(criteriaJson, cancellationToken);

            var responseContent = await InvokeAgentAndAggregateAsync(agent, chatHistory, cancellationToken);
            var result = ParseResponse(responseContent);

            _logger.LogInformation("新闻热点分析完成，推荐股票数量: {Count}", result.Recommendations.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "新闻热点分析失败");
            // 返回带有错误信息的默认结果，供UI展示
            return CreateDefaultResult(ex.Message);
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
        //prompt.AppendLine($"• 需求描述: {request.UserRequirements}");
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
    private StockSelectionResult ParseResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            _logger.LogWarning("响应内容为空，返回默认结果");
            return CreateDefaultResult();
        }
        try
        {
            var result = JsonSerializer.Deserialize<StockSelectionResult>(response, JsonOptions);
            return result ?? CreateDefaultResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "解析响应JSON失败，返回默认结果。原始片段: {Snippet}", SafeSnippet(response));
            return CreateDefaultResult();
        }
    }

    /// <summary>
    /// 创建默认结果
    /// </summary>
    private StockSelectionResult CreateDefaultResult(string? problem = null)
    {
        return new StockSelectionResult
        {
            Recommendations = new List<StockRecommendation>(),
            ConfidenceScore = 0,
            AnalysisSummary = problem ?? "分析过程中遇到问题，请稍后重试。"
        };
    }

    /// <summary>
    /// 安全截取字符串片段用于日志
    /// </summary>
    private static string SafeSnippet(string text, int max = 200)
        => string.IsNullOrEmpty(text) ? string.Empty : (text.Length <= max ? text : text.Substring(0, max) + "...");

    /// <summary>
    /// 汇总 Agent 流式输出
    /// </summary>
    private static async Task<string> InvokeAgentAndAggregateAsync(ChatCompletionAgent agent, ChatHistory history, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        await foreach (var item in agent.InvokeAsync(history, cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrEmpty(item.Message?.Content))
                sb.Append(item.Message.Content);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 统一构建/更新 Agent
    /// </summary>
    private ChatCompletionAgent CreateOrUpdateAgent(
        ChatCompletionAgent? existing,
        string name,
        string description,
        string instructions,
        int maxTokens,
        double temperature,
        string? criteriaJson)
    {
        // 如果已有且不需要更新 criteria 则直接返回
        if (existing != null && string.IsNullOrEmpty(criteriaJson))
            return existing;

        var settings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
            Temperature = temperature,
            MaxTokens = maxTokens
        };

        var args = new KernelArguments(settings);
        if (!string.IsNullOrWhiteSpace(criteriaJson))
            args["criteria"] = criteriaJson;

        return new ChatCompletionAgent
        {
            Name = name,
            Description = description,
            Instructions = instructions,
            Kernel = _kernel!,
            Arguments = args
        };
    }

    /// <summary>
    /// 通用：执行 YAML Prompt -> 得到 criteria JSON
    /// </summary>
    private async Task<string> BuildCriteriaJsonAsync(
        string yamlFileName,
        Action<KernelArguments> argumentBuilder,
        int maxTokens,
        CancellationToken cancellationToken)
    {
        if (!TryEnsureKernel(out var ensureError))
            throw new InvalidOperationException($"Kernel 未初始化：{ensureError}");

        string yamlPath = Path.Combine(Directory.GetCurrentDirectory(), "Plugins", "Yaml", yamlFileName);
        if (!File.Exists(yamlPath))
        {
            _logger.LogWarning("YAML 文件不存在: {YamlPath}", yamlPath);
            throw new FileNotFoundException($"YAML 文件不存在: {yamlFileName}", yamlPath);
        }

        string yamlContent = await File.ReadAllTextAsync(yamlPath, cancellationToken);
        var templateConfig = KernelFunctionYaml.ToPromptTemplateConfig(yamlContent);
        var kernelFunction = KernelFunctionFactory.CreateFromPrompt(templateConfig);

        var execSettings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.None(),
            ResponseFormat = "json_object",
            Temperature = 0.1,
            MaxTokens = maxTokens
        };

        var args = new KernelArguments(execSettings);
        argumentBuilder(args);

        var criteriaResult = await kernelFunction.InvokeAsync(_kernel!, args, cancellationToken: cancellationToken);
        var criteriaJson = criteriaResult?.GetValue<string>() ?? string.Empty;
        _logger.LogInformation("转换完成[{Yaml}]，生成筛选条件JSON长度: {Length}", yamlFileName, criteriaJson.Length);
        return criteriaJson;
    }

    /// <summary>
    /// 确保 Kernel 初始化并添加插件（惰性）
    /// </summary>
    private bool TryEnsureKernel(out string error)
    {
        error = string.Empty;
        if (_kernel != null) return true;

        try
        {
            if (!_kernelFactory.TryCreateKernel(out var baseKernel, out var svcError))
            {
                throw new InvalidOperationException($"无法获取基础 Kernel: {svcError}");
            }
            _kernel = baseKernel.Clone();

            // 添加插件
            var playwrightService = _serviceProvider.GetRequiredService<PlaywrightService>();
            var pluginLogger = _serviceProvider.GetRequiredService<ILogger<StockScreenerPlugin>>();
            var stockScreenerPlugin = new StockScreenerPlugin(playwrightService, pluginLogger);
            _kernel.Plugins.AddFromObject(stockScreenerPlugin);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化 Kernel 失败");
            error = ex.Message;
            _kernel = null;
            return false;
        }
    }

    /// <summary>
    /// 获取新闻分析指令
    /// </summary>
    private string GetNewsAnalysisInstructions()
    {
        return @"
你是一位专业的新闻热点分析师和投资顾问，擅长基于新闻热点提供股票投资建议。

## 核心任务
1. 使用提供的筛选条件调用股票筛选工具：{{StockScreenerPlugin.screen_stocks $criteria}}
2. 分析筛选出的股票结果，结合新闻热点内容
3. 识别与新闻相关的投资机会和受益股票
4. 评估新闻热点的持续性和市场影响
5. 提供基于新闻驱动的投资建议

## 输出格式
请仅返回纯JSON数据，不要包含任何markdown代码块标识（如```json或```）。
包含以下字段：

### 必需字段
- analysisSummary: 字符串，新闻分析总结
- hotspotAnalysis: 字符串，热点分析和趋势判断
- marketImpact: 字符串，对市场的影响分析
- recommendations: 数组，推荐股票列表
- riskWarnings: 字符串数组，风险提示
- investmentStrategy: 字符串，投资策略建议
- confidenceScore: 数值(0-100)，整体置信度评分

### recommendations 数组元素
- symbol: 字符串，股票代码
- name: 字符串，股票名称
- recommendationScore: 数值(0-100)，推荐评分
- reason: 字符串，推荐理由（必须包含与新闻热点的关联性）
- expectedReturn: 数值(如: 15.5)，预期收益率
- riskLevel: 字符串，只能是 ""低风险""、""中风险""、""高风险"" 之一
- newsRelevance: 字符串，与新闻的相关性说明

## 关键注意事项
⚠️ expectedReturn 必须是数值类型
⚠️ 所有评分字段使用数值，不加引号
⚠️ 必须说明每只股票与新闻热点的具体关联
⚠️ 推荐股票数量控制在3-8只
⚠️ 确保JSON格式正确，避免多余的逗号或语法错误

## 严格按照如下格式输出JSON
{
  ""analysisSummary"": ""基于新闻热点的投资机会分析"",
  ""hotspotAnalysis"": ""新闻热点的市场影响和持续性分析"",
  ""marketImpact"": ""对相关行业和市场的影响评估"",
  ""recommendations"": [
    {
      ""symbol"": ""000001"",
      ""name"": ""平安银行"",
      ""recommendationScore"": 85,
      ""reason"": ""银行板块政策利好，业绩稳定增长"",
      ""expectedReturn"": 12.5,
      ""riskLevel"": ""中风险"",
      ""newsRelevance"": ""直接受益于央行政策调整""
    }
  ],
  ""riskWarnings"": [""政策变化风险"", ""市场波动风险""],
  ""investmentStrategy"": ""建议关注政策受益标的，分批建仓"",
  ""confidenceScore"": 80
}
        ";
    }

    /// <summary>
    /// 获取用户需求分析指令
    /// </summary>
    private string GetUserRequirementAnalysisInstructions()
    {
        return @"
你是一位专业的投资顾问，擅长根据用户的需求提供投资建议。

## 核心任务
1. 使用提供的筛选条件调用股票筛选工具：{{StockScreenerPlugin.screen_stocks $criteria}}
2. 分析筛选出的股票结果
3. 根据用户风险偏好选择合适的推荐股票
4. 基于用户需求提供投资建议和推荐理由

## 输出格式
请仅返回纯JSON数据，不要包含任何markdown代码块标识（如```json或```）。
包含以下字段：

### 必需字段
- analysisSummary: 字符串，分析总结
- marketEnvironmentAnalysis: 字符串，市场环境分析
- recommendations: 数组，推荐股票列表
- riskWarnings: 字符串数组，风险提示
- investmentAdvice: 字符串，投资建议
- confidenceScore: 数值(0-100)，置信度评分

### recommendations 数组元素
- symbol: 字符串，股票代码
- name: 字符串，股票名称
- recommendationScore: 数值(0-100)，推荐评分
- reason: 字符串，推荐理由
- expectedReturn: 数值(如: 15.5)，预期收益率
- riskLevel: 字符串，只能是 ""低风险""、""中风险""、""高风险"" 之一

## 关键注意事项
⚠️ expectedReturn 必须是数值类型
⚠️ 所有评分字段使用数值，不加引号
⚠️ 确保JSON格式正确，避免多余的逗号或语法错误

## 严格按照如下格式输出JSON
{
  ""analysisSummary"": ""基于筛选条件分析优质股票"",
  ""marketEnvironmentAnalysis"": ""市场震荡整理阶段"",
  ""recommendations"": [
    {
      ""symbol"": ""000001"",
      ""name"": ""平安银行"",
      ""recommendationScore"": 85,
      ""reason"": ""银行龙头，ROE稳定"",
      ""expectedReturn"": 12.5,
      ""riskLevel"": ""中风险""
    }
  ],
  ""riskWarnings"": [""市场波动风险""],
  ""investmentAdvice"": ""建议分批建仓"",
  ""confidenceScore"": 80
}
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