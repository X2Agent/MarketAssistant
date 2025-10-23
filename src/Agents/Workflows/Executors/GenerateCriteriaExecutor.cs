using MarketAssistant.Infrastructure.Factories;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace MarketAssistant.Agents.Workflows;

/// <summary>
/// 步骤1: 生成股票筛选条件的 Executor
/// 将用户需求或新闻内容转换为结构化的筛选条件 JSON
/// </summary>
internal sealed class GenerateCriteriaExecutor : 
    ReflectingExecutor<GenerateCriteriaExecutor>("GenerateCriteria"),
    IMessageHandler<StockSelectionWorkflowRequest, string>
{
    private readonly IKernelFactory _kernelFactory;
    private readonly ILogger<GenerateCriteriaExecutor> _logger;

    private const string UserRequirementYaml = "user_requirement_to_stock_criteria.yaml";
    private const string NewsAnalysisYaml = "news_analysis_to_stock_criteria.yaml";

    public GenerateCriteriaExecutor(
        IKernelFactory kernelFactory,
        ILogger<GenerateCriteriaExecutor> logger)
    {
        _kernelFactory = kernelFactory ?? throw new ArgumentNullException(nameof(kernelFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<string> HandleAsync(
        StockSelectionWorkflowRequest input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[步骤1/3] 将{Type}转换为筛选条件", 
            input.IsNewsAnalysis ? "新闻内容" : "用户需求");

        try
        {
            // 选择对应的 YAML 模板
            string yamlFileName = input.IsNewsAnalysis ? NewsAnalysisYaml : UserRequirementYaml;
            string yamlPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Agents", "Plugins", "Yaml",
                yamlFileName
            );

            if (!File.Exists(yamlPath))
            {
                throw new FileNotFoundException($"YAML 文件不存在: {yamlFileName}", yamlPath);
            }

            // 加载 YAML 模板
            string yamlContent = await File.ReadAllTextAsync(yamlPath, cancellationToken);
            var templateConfig = KernelFunctionYaml.ToPromptTemplateConfig(yamlContent);
            var kernelFunction = KernelFunctionFactory.CreateFromPrompt(templateConfig);

            // 创建 Kernel
            if (!_kernelFactory.TryCreateKernel(out var kernel, out var error))
            {
                throw new InvalidOperationException($"Kernel 创建失败: {error}");
            }

            // 配置执行参数（禁用工具调用，纯生成 JSON）
            var execSettings = new OpenAIPromptExecutionSettings
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.None(),
                ResponseFormat = "json_object",
                Temperature = 0.1,
                MaxTokens = 2000
            };

            var args = new KernelArguments(execSettings);

            // 根据分析类型设置参数
            if (input.IsNewsAnalysis)
            {
                args["news_content"] = input.NewsContent ?? "";
                args["limit"] = input.MaxRecommendations;
            }
            else
            {
                args["user_requirements"] = input.UserRequirements ?? "";
                args["limit"] = input.MaxRecommendations;
            }

            // 执行 Prompt 生成筛选条件
            var result = await kernelFunction.InvokeAsync(kernel, args, cancellationToken: cancellationToken);
            string criteriaJson = result?.GetValue<string>() ?? "{}";

            _logger.LogInformation("[步骤1/3] 筛选条件生成完成，JSON长度: {Length}", criteriaJson.Length);

            return criteriaJson;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[步骤1/3] 生成筛选条件失败");
            throw new InvalidOperationException($"生成筛选条件失败: {ex.Message}", ex);
        }
    }
}

