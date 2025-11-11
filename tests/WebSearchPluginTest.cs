using MarketAssistant.Agents.Plugins;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Rag.Interfaces;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TestMarketAssistant;

/// <summary>
/// Web 搜索插件测试
/// 注意：此测试已迁移到 Microsoft.Extensions.AI 框架，不再使用 Semantic Kernel
/// </summary>
[TestClass]
public class WebSearchPluginTest : BaseAgentTest
{
    private IChatClient _chatClient = null!;

    [TestInitialize]
    public void Initialize()
    {
        BaseInitialize();
        _chatClient = _chatClientFactory.CreateClient();
    }

    [TestMethod]
    [Timeout(120000)] // 设置2分钟超时
    public async Task TestWebSearchPluginAsync()
    {
        // Arrange
        var userSettingService = _serviceProvider.GetRequiredService<IUserSettingService>();
        var userSetting = userSettingService.CurrentSetting;

        // 确保启用了Web搜索功能并设置了API密钥
        Assert.IsTrue(userSetting.EnableWebSearch, "Web搜索未启用");
        Assert.IsFalse(string.IsNullOrWhiteSpace(userSetting.WebSearchApiKey), "Web搜索API密钥未设置");

        // 创建 GroundingSearchPlugin 实例
        var orchestrator = _serviceProvider.GetRequiredService<IRetrievalOrchestrator>();
        var webTextSearchFactory = _serviceProvider.GetRequiredService<IWebTextSearchFactory>();
        var logger = _serviceProvider.GetService<ILogger<GroundingSearchPlugin>>();
        var groundingSearchPlugin = new GroundingSearchPlugin(orchestrator, webTextSearchFactory, userSettingService, logger!);
        
        // 转换为 AIFunction
        var plugin = Microsoft.SemanticKernel.KernelPluginFactory.CreateFromObject(groundingSearchPlugin);
        var tools = plugin.AsAIFunctions().Cast<AITool>().ToList();

        // Act
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "请搜索关于 Microsoft 的最新新闻")
        };

        var response = await _chatClient.GetResponseAsync(messages, new ChatOptions
        {
            Tools = tools,
            Temperature = 0
        });

        // Assert
        Assert.IsNotNull(response, "响应不能为null");
        Assert.IsFalse(string.IsNullOrWhiteSpace(response.Text), "响应内容不能为空");

        Console.WriteLine($"搜索响应: {response.Text}");

        // 验证响应是否包含相关内容
        var content = response.Text?.ToLower();
        Assert.IsTrue(
            content?.Contains("microsoft") == true || 
            content?.Contains("微软") == true ||
            content?.Contains("搜索") == true,
            "响应应该包含与搜索相关的内容");
    }
}