using MarketAssistant.Agents;
using MarketAssistant.Infrastructure.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace TestMarketAssistant;

/// <summary>
/// ChatClient 测试 - 基于 Agent Framework
/// </summary>
[TestClass]
public class ChatCompletionTest : BaseAgentTest
{
    private IChatClient _chatClient = null!;
    private IAgentToolsConfig _toolsConfig = null!;

    [TestInitialize]
    public void Initialize()
    {
        BaseInitialize(); // 调用基类初始化方法
        _chatClient = _chatClientFactory.CreateClient();
        _toolsConfig = _serviceProvider.GetRequiredService<IAgentToolsConfig>();
    }

    [TestMethod]
    public async Task TestChatAsync()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "股票 sz002594 的价格")
        };

        var tools = _toolsConfig.GetToolsForAgent(AnalysisAgent.FundamentalAnalyst);
        
        var response = await _chatClient.GetResponseAsync(messages, new ChatOptions
        {
            Tools = tools
        });

        Assert.IsNotNull(response);
        Console.WriteLine(response.Text);
    }

    [TestMethod]
    public async Task TestMACDAsync()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "获取股票 sz002594 的K线日线MACD数据")
        };

        var tools = _toolsConfig.GetToolsForAgent(AnalysisAgent.TechnicalAnalyst);
        
        var response = await _chatClient.GetResponseAsync(messages, new ChatOptions
        {
            Tools = tools
        });

        Assert.IsNotNull(response);
        Console.WriteLine(response.Text);
    }
}
