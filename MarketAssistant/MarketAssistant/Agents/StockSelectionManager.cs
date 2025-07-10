using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Text;

namespace MarketAssistant.Agents;

/// <summary>
/// AI选股管理器，负责创建和管理AI选股代理
/// </summary>
public class StockSelectionManager
{
    private readonly Kernel _kernel;
    private ChatCompletionAgent? _stockSelectionAgent;

    public StockSelectionManager(Kernel kernel)
    {
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
    }

    /// <summary>
    /// 创建AI选股代理
    /// </summary>
    /// <returns>AI选股代理实例</returns>
    public async Task<ChatCompletionAgent> CreateStockSelectionAgentAsync()
    {
        if (_stockSelectionAgent != null)
        {
            return _stockSelectionAgent;
        }

        try
        {
            // 加载代理配置
            var agentYamlPath = FindAgentYamlPath();

            if (!File.Exists(agentYamlPath))
            {
                throw new FileNotFoundException($"找不到AI选股代理配置文件: {agentYamlPath}");
            }

            string yamlContent = File.ReadAllText(agentYamlPath);
            PromptTemplateConfig templateConfig = KernelFunctionYaml.ToPromptTemplateConfig(yamlContent);

            var promptExecutionSettings = new OpenAIPromptExecutionSettings()
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(options: new()
                {
                    AllowParallelCalls = false,
                    AllowStrictSchemaAdherence = false,
                    RetainArgumentTypes = true
                })
            };

            _stockSelectionAgent = new ChatCompletionAgent()
            {
                Name = templateConfig.Name,
                Description = templateConfig.Description,
                Instructions = templateConfig.Template,
                Kernel = _kernel,
                Arguments = new KernelArguments(promptExecutionSettings)
                {
                    { "global_analysis_guidelines", GetGlobalAnalysisGuidelines() },
                }
            };

            return _stockSelectionAgent;
        }
        catch (Exception ex)
        {
            throw new Exception($"创建AI选股代理失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 执行AI选股分析
    /// </summary>
    /// <param name="userRequirements">用户选股需求</param>
    /// <returns>选股分析结果</returns>
    public async Task<string> ExecuteStockSelectionAsync(string userRequirements)
    {
        if (string.IsNullOrWhiteSpace(userRequirements))
        {
            throw new ArgumentException("用户选股需求不能为空", nameof(userRequirements));
        }

        try
        {
            // 创建选股代理
            var agent = await CreateStockSelectionAgentAsync();

            // 创建聊天历史
            var chatHistory = new ChatHistory();

            // 构建选股请求消息
            var requestMessage = BuildSelectionRequestMessage(userRequirements);
            chatHistory.AddUserMessage(requestMessage);

            // 执行选股分析
            var response = await agent.InvokeAsync(chatHistory).ToListAsync();

            return response.LastOrDefault()?.Message.Content ?? "未能生成选股结果";
        }
        catch (Exception ex)
        {
            throw new Exception($"执行AI选股分析失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 执行快速选股（预设策略）
    /// </summary>
    /// <param name="strategy">选股策略</param>
    /// <returns>选股分析结果</returns>
    public async Task<string> ExecuteQuickSelectionAsync(QuickSelectionStrategy strategy)
    {
        var requirements = strategy switch
        {
            QuickSelectionStrategy.ValueStocks => "请筛选价值股：PE低于20，PB低于2，市值大于100亿，ROE大于10%的优质价值股",
            QuickSelectionStrategy.GrowthStocks => "请筛选成长股：营收增长率大于20%，净利润增长率大于15%，市值在50-500亿之间的成长股",
            QuickSelectionStrategy.ActiveStocks => "请筛选活跃股：换手率大于3%，成交额大于5亿，近期涨跌幅在-5%到10%之间的活跃股票",
            QuickSelectionStrategy.LargeCap => "请筛选大盘股：市值大于1000亿，流动性好，业绩稳定的大盘蓝筹股",
            QuickSelectionStrategy.SmallCap => "请筛选小盘股：市值在20-200亿之间，具有成长潜力的小盘股",
            QuickSelectionStrategy.Dividend => "请筛选高股息股：股息率大于3%，连续分红3年以上，现金流稳定的高股息股票",
            _ => throw new ArgumentException($"不支持的选股策略: {strategy}")
        };

        return await ExecuteStockSelectionAsync(requirements);
    }

    /// <summary>
    /// 构建选股请求消息
    /// </summary>
    private string BuildSelectionRequestMessage(string userRequirements)
    {
        var messageBuilder = new StringBuilder();
        messageBuilder.AppendLine("请根据以下用户需求进行AI选股分析：");
        messageBuilder.AppendLine();
        messageBuilder.AppendLine($"**用户需求：** {userRequirements}");
        messageBuilder.AppendLine();
        messageBuilder.AppendLine("请按照以下步骤进行分析：");
        messageBuilder.AppendLine("1. 分析用户需求，确定筛选策略");
        messageBuilder.AppendLine("2. 使用相应的筛选函数获取候选股票");
        messageBuilder.AppendLine("3. 对候选股票进行综合评估和排序");
        messageBuilder.AppendLine("4. 提供详细的选股结果和投资建议");
        messageBuilder.AppendLine();
        messageBuilder.AppendLine("请确保输出包含：候选股票池、推荐理由、风险等级、投资建议和风险提示。");

        return messageBuilder.ToString();
    }

    /// <summary>
    /// 查找代理YAML文件路径
    /// </summary>
    private string FindAgentYamlPath()
    {
        // 尝试多个可能的路径
        var possiblePaths = new[]
        {
            // 运行时路径
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Agents", "yaml", "StockSelectionAgent.yaml"),
            // 项目根目录路径（用于测试环境）
            Path.Combine(Directory.GetCurrentDirectory(), "MarketAssistant", "MarketAssistant", "Agents", "yaml", "StockSelectionAgent.yaml"),
            // 相对路径（用于开发环境）
            Path.Combine("Agents", "yaml", "StockSelectionAgent.yaml"),
            // 向上查找项目路径
            FindProjectPath()
        };

        foreach (var path in possiblePaths)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                return path;
            }
        }

        // 如果都找不到，返回默认路径
        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Agents", "yaml", "StockSelectionAgent.yaml");
    }

    /// <summary>
    /// 查找项目路径
    /// </summary>
    private string FindProjectPath()
    {
        var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());

        // 向上查找包含MarketAssistant项目的目录
        while (currentDir != null)
        {
            var projectPath = Path.Combine(currentDir.FullName, "MarketAssistant", "MarketAssistant", "Agents", "yaml", "StockSelectionAgent.yaml");
            if (File.Exists(projectPath))
            {
                return projectPath;
            }
            currentDir = currentDir.Parent;
        }

        return null;
    }

    /// <summary>
    /// 获取全局分析准则
    /// </summary>
    private string GetGlobalAnalysisGuidelines()
    {
        return @"
        ## 全局分析准则
        
        1. **客观性原则**：基于真实数据进行分析，避免主观臆断
        2. **风险意识**：充分评估和提示投资风险
        3. **专业性**：使用准确的金融术语和分析方法
        4. **实用性**：提供可操作的投资建议
        5. **及时性**：反映最新的市场变化和数据
        6. **合规性**：遵守相关法律法规，不提供内幕信息
        7. **教育性**：帮助用户理解投资逻辑和风险
        
        ## 免责声明
        本分析仅供参考，不构成投资建议。投资有风险，入市需谨慎。
        ";
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        // ChatCompletionAgent 不需要手动释放资源
        _stockSelectionAgent = null;
    }
}

/// <summary>
/// 快速选股策略枚举
/// </summary>
public enum QuickSelectionStrategy
{
    /// <summary>
    /// 价值股
    /// </summary>
    ValueStocks,

    /// <summary>
    /// 成长股
    /// </summary>
    GrowthStocks,

    /// <summary>
    /// 活跃股
    /// </summary>
    ActiveStocks,

    /// <summary>
    /// 大盘股
    /// </summary>
    LargeCap,

    /// <summary>
    /// 小盘股
    /// </summary>
    SmallCap,

    /// <summary>
    /// 高股息股
    /// </summary>
    Dividend
}