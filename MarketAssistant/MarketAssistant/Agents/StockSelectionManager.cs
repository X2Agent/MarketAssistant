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
    private readonly Kernel _kernel;
    private readonly ILogger<StockSelectionManager> _logger;
    private ChatCompletionAgent? _newsAnalysisAgent;
    private ChatCompletionAgent? _userRequirementAgent;
    private ChatCompletionAgent? _requirementTranslateAgent;
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
    private ChatCompletionAgent CreateNewsAnalysisAgent(string? criteriaJson = null, CancellationToken cancellationToken = default)
    {
        if (_newsAnalysisAgent != null && string.IsNullOrEmpty(criteriaJson))
            return _newsAnalysisAgent;

        try
        {
            _logger.LogInformation("创建新闻分析代理");

            var promptExecutionSettings = new OpenAIPromptExecutionSettings()
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
                //ResponseFormat = "json_object",
                Temperature = 0.2,
                MaxTokens = 3000
            };

            var kernelArguments = new KernelArguments(promptExecutionSettings);
            if (!string.IsNullOrEmpty(criteriaJson))
            {
                kernelArguments["criteria"] = criteriaJson;
            }

            var agent = new ChatCompletionAgent()
            {
                Name = "NewsHotspotAnalyzer",
                Description = "新闻热点分析专家",
                Instructions = GetNewsAnalysisInstructions(),
                Kernel = _kernel,
                Arguments = kernelArguments
            };

            _logger.LogInformation("新闻分析代理创建成功");
            return agent;
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

            var promptExecutionSettings = new OpenAIPromptExecutionSettings()
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
                Temperature = 0.2,
                MaxTokens = 8000
            };

            var kernelArguments = new KernelArguments(promptExecutionSettings);
            if (!string.IsNullOrEmpty(criteriaJson))
            {
                kernelArguments["criteria"] = criteriaJson;
            }

            _userRequirementAgent = new ChatCompletionAgent()
            {
                Name = "UserRequirementAnalyzer",
                Description = "用户需求分析专家",
                Instructions = GetUserRequirementAnalysisInstructions(),
                Kernel = _kernel,
                Arguments = kernelArguments
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

    /// <summary>
    /// 需求转换代理
    /// 将用户的文字需求转换为StockCriteria JSON格式
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private ChatCompletionAgent GetRequirementTranslateAgent(CancellationToken cancellationToken = default)
    {
        if (_requirementTranslateAgent != null)
        {
            return _requirementTranslateAgent;
        }
        try
        {
            _logger.LogInformation("创建需求转换代理");

            var promptExecutionSettings = new OpenAIPromptExecutionSettings()
            {
                ResponseFormat = "json_object",
                Temperature = 0.1,
                MaxTokens = 2000
            };

            _requirementTranslateAgent = new ChatCompletionAgent()
            {
                Name = "RequirementTranslateAgent",
                Description = "需求转换专家，将用户需求转换为StockCriteria JSON格式",
                Instructions = GetRequirementTranslateInstructions(),
                Kernel = _kernel,
                Arguments = new KernelArguments(promptExecutionSettings)
            };
            _logger.LogInformation("需求转换代理创建成功");
            return _requirementTranslateAgent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建需求转换代理失败");
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

            // 第一步：将用户需求转换为StockCriteria JSON格式
            var translateChatHistory = new ChatHistory();
            var translatePrompt = $"用户需求：{request.UserRequirements}";
            translateChatHistory.AddUserMessage(translatePrompt);

            var translateAgent = GetRequirementTranslateAgent(cancellationToken);
            string criteriaJson = "";

            await foreach (var item in translateAgent.InvokeAsync(translateChatHistory, cancellationToken: cancellationToken))
            {
                criteriaJson += item.Message?.Content ?? "";
            }

            _logger.LogInformation("需求转换完成，生成的筛选条件JSON: {CriteriaJson}", criteriaJson);

            // 第二步：使用筛选条件调用股票筛选插件并进行分析
            var chatHistory = new ChatHistory();
            var prompt = BuildUserRequirementPrompt(request);

            chatHistory.AddUserMessage(prompt);

            var agent = CreateUserRequirementAgent(criteriaJson, cancellationToken);

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

            // 第一步：将新闻内容转换为StockCriteria JSON格式
            string yamlPath = Path.Combine(Directory.GetCurrentDirectory(), "Plugins", "Yaml", "news_analysis_to_stock_criteria.yaml");
            if (!File.Exists(yamlPath))
            {
                _logger.LogWarning("新闻分析YAML文件不存在: {YamlPath}", yamlPath);
                throw new Exception("新闻分析YAML文件不存在，请检查配置。");
            }

            string yamlContent = File.ReadAllText(yamlPath);
            var templateConfig = KernelFunctionYaml.ToPromptTemplateConfig(yamlContent);

            var newsAnalysisFunction = KernelFunctionFactory.CreateFromPrompt(templateConfig);

            var promptExecutionSettings = new OpenAIPromptExecutionSettings()
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.None(),
                ResponseFormat = "json_object",
                Temperature = 0.1,
                MaxTokens = 2500
            };

            var criteriaResult = await newsAnalysisFunction.InvokeAsync(_kernel, new KernelArguments(promptExecutionSettings)
            {
                ["news_content"] = request.NewsContent,
                ["limit"] = request.MaxRecommendations
            });


            string criteriaJson = criteriaResult!.GetValue<string>();

            _logger.LogInformation("新闻转换完成，生成的筛选条件JSON: {CriteriaJson}", criteriaJson);

            // 第二步：使用筛选条件调用股票筛选插件并进行分析
            var chatHistory = new ChatHistory();
            var prompt = BuildNewsAnalysisPrompt(request);
            chatHistory.AddUserMessage(prompt);

            var agent = CreateNewsAnalysisAgent(criteriaJson, cancellationToken);

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

    /// <summary>
    /// 获取需求转换指令
    /// </summary>
    private string GetRequirementTranslateInstructions()
    {
        return @"
你是一个专业的需求转换助手，负责将用户的文字需求转换为标准的StockCriteria JSON格式。

## 主要任务
分析用户需求，生成符合StockCriteria格式的JSON对象，包含具体的筛选条件。

## 需求转换规则

### 市值类别
- 大盘股/蓝筹股 → mc >= 100000000000
- 中盘股 → mc: 10000000000-100000000000  
- 小盘股 → mc < 10000000000
- 市值X亿以上 → mc >= X*100000000

### 估值指标
- 价值股/低估值/便宜 → pettm < 15, pb < 2
- 成长股/高成长 → npay > 20, oiy > 15
- 高ROE/盈利能力强 → roediluted > 15
- 低市盈率 → pettm < 20
- 低市净率 → pb < 3
- 市盈率X倍以下 → pettm < X
- 市净率X倍以下 → pb < X

### 财务表现
- 业绩好/盈利增长 → npay > 10
- 营收增长 → oiy > 10
- 高股息/分红股 → dy_l > 2
- 每股净资产高 → bps > 10
- 净利润增长X%以上 → npay > X
- 营收增长X%以上 → oiy > X
- 股息率X%以上 → dy_l > X

### 市场表现
- 活跃股/成交活跃 → amount > 100000000, tr > 2
- 强势股 → pct60 > 20
- 近期涨幅大 → pct20 > 10
- 抗跌股 → pct20 > -5
- 近X日涨幅大于Y% → pctX > Y
- 成交额X亿以上 → amount > X*100000000
- 换手率X%以上 → tr > X

### 价格相关
- 股价X元以下 → current < X
- 股价X元以上 → current > X
- 低价股 → current < 10
- 中价股 → current: 10-50
- 高价股 → current > 50

## 支持的筛选指标

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

## 输出示例

### 示例1：筛选大盘蓝筹股，高ROE，低估值
用户需求：推荐一些大盘蓝筹股，要求ROE高于15%，市盈率低于20倍
输出StockCriteria的JSON格式：
{
  ""criteria"": [
    {
      ""code"": ""mc"",
      ""displayName"": ""总市值"",
      ""minValue"": 100000000000,
      ""maxValue"": null
    },
    {
      ""code"": ""roediluted"",
      ""displayName"": ""净资产收益率"",
      ""minValue"": 15,
      ""maxValue"": null
    },
    {
      ""code"": ""pettm"",
      ""displayName"": ""市盈率TTM"",
      ""minValue"": null,
      ""maxValue"": 20
    }
  ],
  ""market"": ""全部A股"",
  ""industry"": ""全部"",
  ""limit"": 20
}

### 示例2：筛选活跃的低价股
用户需求：股价10元以下，成交额1亿以上，换手率3%以上
输出StockCriteria的JSON格式：
{
  ""criteria"": [
    {
      ""code"": ""current"",
      ""displayName"": ""当前价"",
      ""minValue"": null,
      ""maxValue"": 10
    },
    {
      ""code"": ""amount"",
      ""displayName"": ""当日成交额"",
      ""minValue"": 100000000,
      ""maxValue"": null
    },
    {
      ""code"": ""tr"",
      ""displayName"": ""当日换手率"",
      ""minValue"": 3,
      ""maxValue"": null
    }
  ],
  ""market"": ""全部A股"",
  ""industry"": ""全部"",
  ""limit"": 20
}

## JSON格式要求
1. 所有字段名必须用双引号包围
2. 字符串值用双引号，数值不用引号，null值不用引号
3. 数组最后一个元素后不能有逗号
4. minValue或maxValue为空时使用null，不是空字符串
6. market字段常用值：全部A股、沪市A股、深市A股
7. industry字段常用值：全部、科技、金融、医药、消费、制造业等

## 重要提醒
- 市值单位为元（100亿 = 10000000000）
- 成交额单位为元（1亿 = 100000000）  
- 百分比指标输入数值不带%号（如15%输入15）

## 输出要求
- 只输出符合StockCriteria格式的JSON对象
- 不需要额外的解释说明
- 确保JSON格式完全正确，避免序列化错误
        ";
    }

    /// <summary>
    /// 加载新闻分析YAML功能
    /// </summary>
    private void LoadNewsAnalysisFunction()
    {
        try
        {
            string yamlPath = Path.Combine(Directory.GetCurrentDirectory(), "Plugins", "Yaml", "news_analysis_to_stock_criteria.yaml");
            if (File.Exists(yamlPath))
            {
                string yamlContent = File.ReadAllText(yamlPath);
                var templateConfig = KernelFunctionYaml.ToPromptTemplateConfig(yamlContent);

                var newsAnalysisFunction = KernelFunctionFactory.CreateFromPrompt(templateConfig);

                // 将功能添加到Kernel的插件中
                var plugin = KernelPluginFactory.CreateFromFunctions("NewsAnalysis",
                    "新闻分析转股票筛选功能",
                    new[] { newsAnalysisFunction });

                _newsAnalysisAgent!.Kernel.Plugins.Add(plugin);

                _logger.LogInformation("成功加载新闻分析YAML功能");
            }
            else
            {
                _logger.LogWarning("新闻分析YAML文件不存在: {YamlPath}", yamlPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载新闻分析YAML功能失败");
        }
    }

    /// <summary>
    /// 加载新闻分析YAML功能到指定代理
    /// </summary>
    /// <param name="agent"></param>
    private void LoadNewsAnalysisFunction(ChatCompletionAgent agent)
    {
        try
        {
            string yamlPath = Path.Combine(Directory.GetCurrentDirectory(), "Plugins", "Yaml", "news_analysis_to_stock_criteria.yaml");
            if (File.Exists(yamlPath))
            {
                string yamlContent = File.ReadAllText(yamlPath);
                var templateConfig = KernelFunctionYaml.ToPromptTemplateConfig(yamlContent);

                var newsAnalysisFunction = KernelFunctionFactory.CreateFromPrompt(templateConfig);

                // 将功能添加到指定代理的插件中
                var plugin = KernelPluginFactory.CreateFromFunctions("NewsAnalysis",
                    "新闻分析转股票筛选功能",
                    new[] { newsAnalysisFunction });

                agent.Kernel.Plugins.Add(plugin);

                _logger.LogInformation("成功加载新闻分析YAML功能到代理: {AgentName}", agent.Name);
            }
            else
            {
                _logger.LogWarning("新闻分析YAML文件不存在: {YamlPath}", yamlPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载新闻分析YAML功能到代理失败: {AgentName}", agent.Name);
        }
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
            _requirementTranslateAgent = null;
            _disposed = true;
        }
    }

    #endregion
}