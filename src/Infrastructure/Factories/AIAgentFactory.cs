using MarketAssistant.Agents;
using MarketAssistant.Infrastructure.Configuration;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

namespace MarketAssistant.Infrastructure.Factories;

/// <summary>
/// 配置好的聊天客户端包装器
/// 包含客户端实例和建议的指令
/// </summary>
public class ConfiguredChatClient
{
    public IChatClient Client { get; init; } = null!;
    public string Instructions { get; init; } = string.Empty;
    public IList<AIFunction> Tools { get; init; } = Array.Empty<AIFunction>();
}

/// <summary>
/// AI Agent 工厂接口
/// 负责创建配置好模型和工具的聊天客户端
/// </summary>
public interface IAIAgentFactory
{
    ConfiguredChatClient CreateAgent(AnalysisAgents agentType, string? instructions = null);
    ConfiguredChatClient CreateChatAgent(string instructions, IList<AIFunction>? tools = null);
    bool TryCreateAgent(AnalysisAgents agentType, out ConfiguredChatClient agent, out string error, string? instructions = null);
    void Invalidate();
}

/// <summary>
/// AI Agent 工厂实现
/// 创建配置好模型和工具的聊天客户端包装器
/// </summary>
public class AIAgentFactory : IAIAgentFactory
{
    private readonly IUserSettingService _userSettingService;
    private readonly IAgentToolsConfig _toolsConfig;
    private readonly object _lock = new();
    private IChatClient? _cachedChatClient;
    private string? _lastError;

    public AIAgentFactory(
        IUserSettingService userSettingService,
        IAgentToolsConfig toolsConfig)
    {
        _userSettingService = userSettingService;
        _toolsConfig = toolsConfig;
    }

    /// <summary>
    /// 创建指定类型的分析代理
    /// </summary>
    public ConfiguredChatClient CreateAgent(AnalysisAgents agentType, string? instructions = null)
    {
        if (TryCreateAgent(agentType, out var agent, out var error, instructions))
            return agent;
        throw new FriendlyException(error);
    }

    /// <summary>
    /// 创建自定义的聊天代理
    /// </summary>
    public ConfiguredChatClient CreateChatAgent(string instructions, IList<AIFunction>? tools = null)
    {
        var chatClient = GetOrCreateChatClient();
        
        return new ConfiguredChatClient
        {
            Client = chatClient,
            Instructions = instructions,
            Tools = tools ?? Array.Empty<AIFunction>()
        };
    }

    /// <summary>
    /// 尝试创建指定类型的分析代理
    /// </summary>
    public bool TryCreateAgent(
        AnalysisAgents agentType, 
        out ConfiguredChatClient agent, 
        out string error, 
        string? instructions = null)
    {
        try
        {
            var chatClient = GetOrCreateChatClient();
            var tools = _toolsConfig.GetToolsForAgent(agentType);
            var agentInstructions = instructions ?? GetDefaultInstructions(agentType);

            agent = new ConfiguredChatClient
            {
                Client = chatClient,
                Instructions = agentInstructions,
                Tools = tools
            };

            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            agent = null!;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// 使缓存失效，强制重新创建客户端
    /// </summary>
    public void Invalidate()
    {
        lock (_lock)
        {
            _cachedChatClient = null;
            _lastError = null;
        }
    }

    /// <summary>
    /// 获取或创建底层的 ChatClient
    /// </summary>
    private IChatClient GetOrCreateChatClient()
    {
        lock (_lock)
        {
            if (_cachedChatClient != null)
                return _cachedChatClient;

            if (!string.IsNullOrEmpty(_lastError))
                throw new FriendlyException(_lastError);

            try
            {
                _cachedChatClient = BuildChatClient();
                return _cachedChatClient;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                throw new FriendlyException(_lastError);
            }
        }
    }

    /// <summary>
    /// 构建 ChatClient 实例
    /// </summary>
    private IChatClient BuildChatClient()
    {
        var userSetting = _userSettingService.CurrentSetting;
        
        if (string.IsNullOrWhiteSpace(userSetting.ModelId))
            throw new FriendlyException("AI 功能未配置：请先在设置页面选择 AI 模型");
        if (string.IsNullOrWhiteSpace(userSetting.ApiKey))
            throw new FriendlyException("AI 功能未配置：请先在设置页面配置 API Key");
        if (string.IsNullOrWhiteSpace(userSetting.Endpoint))
            throw new FriendlyException("AI 功能未配置：请先在设置页面配置 API 端点");

        var openAIClient = new OpenAIClient(
            new ApiKeyCredential(userSetting.ApiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(userSetting.Endpoint)
            }
        );

        return openAIClient.GetChatClient(userSetting.ModelId).AsIChatClient();
    }

    /// <summary>
    /// 获取指定代理类型的默认指令
    /// </summary>
    private string GetDefaultInstructions(AnalysisAgents agentType)
    {
        return agentType switch
        {
            AnalysisAgents.FundamentalAnalystAgent => 
                "你是一位专业的基本面分析师，擅长分析公司财务数据、行业地位和竞争优势。",
            
            AnalysisAgents.TechnicalAnalystAgent => 
                "你是一位资深的技术分析师，精通K线形态、技术指标和趋势分析。",
            
            AnalysisAgents.FinancialAnalystAgent => 
                "你是一位财务分析专家，擅长解读财务报表、分析盈利能力和财务健康状况。",
            
            AnalysisAgents.NewsEventAnalystAgent => 
                "你是一位新闻事件分析师，善于捕捉市场热点、解读新闻对股价的影响。",
            
            AnalysisAgents.MarketSentimentAnalystAgent => 
                "你是一位市场情绪分析师，擅长分析市场氛围、投资者情绪和资金流向。",
            
            AnalysisAgents.CoordinatorAnalystAgent => 
                "你是一位综合分析协调员，负责整合各方分析结果，给出全面的投资建议。",
            
            _ => "你是一位专业的金融分析助手。"
        };
    }
}

