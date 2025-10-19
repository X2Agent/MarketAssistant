using MarketAssistant.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Infrastructure.Factories;

/// <summary>
/// MarketChatAgent 工厂接口
/// 用于创建和管理聊天代理实例
/// </summary>
public interface IMarketChatAgentFactory
{
    /// <summary>
    /// 创建新的聊天代理实例
    /// </summary>
    /// <param name="sessionId">可选的会话 ID，用于日志追踪</param>
    /// <returns>新的聊天代理实例</returns>
    MarketChatAgent CreateAgent(string? sessionId = null);
}

/// <summary>
/// MarketChatAgent 工厂实现
/// </summary>
public class MarketChatAgentFactory : IMarketChatAgentFactory
{
    private readonly IAIAgentFactory _aiAgentFactory;
    private readonly ILoggerFactory _loggerFactory;

    public MarketChatAgentFactory(
        IAIAgentFactory aiAgentFactory,
        ILoggerFactory loggerFactory)
    {
        _aiAgentFactory = aiAgentFactory;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// 创建新的聊天代理实例
    /// </summary>
    public MarketChatAgent CreateAgent(string? sessionId = null)
    {
        var systemPrompt = """
            你是一个专业的股票市场分析助手，具备以下能力：
            1. 提供专业的股票分析和投资建议
            2. 解答用户关于股票市场的各种问题
            3. 基于技术分析、基本面分析等多维度提供见解
            4. 主动使用可用的分析工具获取实时数据
            5. 保持客观、专业的态度，提醒投资风险

            工具使用指导：
            - 当需要股票基本信息时，优先使用股票基础信息插件
            - 当需要财务数据时，使用财务分析插件获取准确数据
            - 当需要技术指标时，使用技术分析插件计算指标
            - 当需要最新新闻时，使用新闻搜索插件获取资讯
            - 当需要筛选股票时，使用股票筛选插件

            回复格式要求：
            - 使用结构化格式：【核心观点】、【数据支撑】、【技术分析】、【风险提示】
            - 语言简洁明了，避免过于技术化的术语
            - 提供具体的数据和分析依据
            - 重要数据用**粗体**标注
            - 始终在结尾提醒投资风险
            """;

        var configuredClient = _aiAgentFactory.CreateChatAgent(systemPrompt);
        
        // 创建 Logger，使用标准类型
        var logger = _loggerFactory.CreateLogger<MarketChatAgent>();

        return new MarketChatAgent(configuredClient.Client, logger);
    }
}

