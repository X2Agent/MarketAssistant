using MarketAssistant.Agents;
using MarketAssistant.Infrastructure.Factories;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace TestMarketAssistant;

[TestClass]
public class MarketChatSessionTest : BaseAgentTest
{
    private MarketChatSession _chatSession = null!;

    [TestInitialize]
    public void Initialize()
    {
        BaseInitialize();

        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<MarketChatSession>();

        // 使用 ChatClientFactory 创建 ChatClient
        var chatClientFactory = _serviceProvider.GetRequiredService<IChatClientFactory>();
        var chatClient = chatClientFactory.CreateClient();

        _chatSession = new MarketChatSession(chatClient, logger);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _chatSession?.Dispose();
    }

    [TestMethod]
    public async Task TestBasicChatAsync()
    {
        // 测试基础对话功能（流式响应）
        var responseBuilder = new System.Text.StringBuilder();

        await foreach (var update in _chatSession.SendMessageStreamAsync("你好，请介绍股票投资的基础知识"))
        {
            responseBuilder.Append(update.Content);
        }

        var responseText = responseBuilder.ToString();
        Assert.IsNotNull(responseText);
        Assert.IsFalse(string.IsNullOrEmpty(responseText));

        Console.WriteLine($"AI回复: {responseText}");
    }

    [TestMethod]
    public async Task TestStockContextChatAsync()
    {
        // 设置股票上下文
        _chatSession.SetCurrentStock("sz002594");

        // 测试带有股票上下文的对话
        var responseBuilder = new System.Text.StringBuilder();
        await foreach (var update in _chatSession.SendMessageStreamAsync("这只股票的基本面如何？"))
        {
            responseBuilder.Append(update.Content);
        }

        var responseText = responseBuilder.ToString();
        Assert.IsNotNull(responseText);
        Assert.IsFalse(string.IsNullOrEmpty(responseText));

        Console.WriteLine($"AI回复: {responseText}");
    }

    [TestMethod]
    public async Task TestConversationHistoryAsync()
    {
        // 测试多轮对话
        await foreach (var _ in _chatSession.SendMessageStreamAsync("什么是市盈率？")) { }
        await foreach (var _ in _chatSession.SendMessageStreamAsync("有何意义？")) { }

        var responseBuilder = new System.Text.StringBuilder();
        await foreach (var update in _chatSession.SendMessageStreamAsync("这两个指标有什么区别"))
        {
            responseBuilder.Append(update.Content);
        }

        var responseText = responseBuilder.ToString();
        Assert.IsNotNull(responseText);
        Assert.IsFalse(string.IsNullOrEmpty(responseText));

        // 验证对话历史（异步获取）
        var history = await _chatSession.GetConversationHistoryAsync();
        Assert.IsTrue(history.Count > 0);

        Console.WriteLine($"对话历史条数: {history.Count}");
        Console.WriteLine($"最新回复: {responseText}");
    }

    [TestMethod]
    public async Task TestClearHistoryAsync()
    {
        // 添加一些对话
        await foreach (var _ in _chatSession.SendMessageStreamAsync("测试消息")) { }

        // 验证有历史记录
        var historyBefore = await _chatSession.GetConversationHistoryAsync();
        Assert.IsTrue(historyBefore.Count > 0);

        // 清除历史
        _chatSession.ClearHistory();

        // 验证历史被清空
        var historyAfter = await _chatSession.GetConversationHistoryAsync();
        Assert.AreEqual(0, historyAfter.Count);
    }

    [TestMethod]
    public async Task TestContextWindowManagementAsync()
    {
        // 设置股票上下文
        _chatSession.SetCurrentStock("sz002594");

        // 添加大量消息来测试上下文窗口管理
        for (int i = 0; i < 50; i++)
        {
            await foreach (var _ in _chatSession.SendMessageStreamAsync($"这是第{i}次测试消息，关于sz002594的股票分析。")) { }
        }

        // 测试对话历史是否被管理
        var history = await _chatSession.GetConversationHistoryAsync();
        Assert.IsTrue(history.Count > 0);
        Assert.AreEqual("sz002594", _chatSession.CurrentStockCode);

        Console.WriteLine($"消息数: {history.Count}");
    }

    [TestMethod]
    public async Task TestTopicGuidanceAsync()
    {
        // 设置股票上下文
        _chatSession.SetCurrentStock("sz002594");

        // 询问与股票无关的消息，测试AI是否能自然地引导回相关话题
        var responseBuilder = new System.Text.StringBuilder();
        await foreach (var update in _chatSession.SendMessageStreamAsync("今天的天气怎么样"))
        {
            responseBuilder.Append(update.Content);
        }

        var responseText = responseBuilder.ToString();
        Assert.IsNotNull(responseText);
        Assert.IsFalse(string.IsNullOrEmpty(responseText));
        // AI应该能够自然地回复用户或引导回股票话题

        Console.WriteLine($"AI的回复: {responseText}");
    }

    [TestMethod]
    public async Task TestStreamingResponseAsync()
    {
        // 设置股票上下文
        _chatSession.SetCurrentStock("sz000001");

        // 测试流式响应
        var allContent = new List<string>();
        await foreach (var update in _chatSession.SendMessageStreamAsync("分析sz000001的技术指标"))
        {
            if (!string.IsNullOrEmpty(update.Content))
            {
                allContent.Add(update.Content);
            }
        }

        Assert.IsTrue(allContent.Count > 0);

        var fullResponse = string.Join("", allContent);
        Console.WriteLine($"流式响应完整内容: {fullResponse}");
    }

    [TestMethod]
    public void TestCancellationAsync()
    {
        var cts = new CancellationTokenSource();

        // 立即取消
        _chatSession.StopCurrentRequest();
        cts.Cancel();

        // 验证取消状态
        Assert.IsTrue(_chatSession.IsProcessing == false);
    }

    [TestMethod]
    public async Task TestIntelligentAnalysisAsync()
    {
        // 设置股票上下文
        _chatSession.SetCurrentStock("sz002594");

        // 测试AI能否智能调用可能的插件来回答深度问题
        var response1Builder = new System.Text.StringBuilder();
        await foreach (var update in _chatSession.SendMessageStreamAsync("分析MACD和RSI指标"))
        {
            response1Builder.Append(update.Content);
        }
        Assert.IsFalse(string.IsNullOrEmpty(response1Builder.ToString()));

        var response2Builder = new System.Text.StringBuilder();
        await foreach (var update in _chatSession.SendMessageStreamAsync("这家公司的ROE和净利润如何？"))
        {
            response2Builder.Append(update.Content);
        }
        Assert.IsFalse(string.IsNullOrEmpty(response2Builder.ToString()));

        var response3Builder = new System.Text.StringBuilder();
        await foreach (var update in _chatSession.SendMessageStreamAsync("投资这只股票有什么风险？"))
        {
            response3Builder.Append(update.Content);
        }
        Assert.IsFalse(string.IsNullOrEmpty(response3Builder.ToString()));

        Console.WriteLine($"技术分析回复: {response1Builder}");
        Console.WriteLine($"财务分析回复: {response2Builder}");
        Console.WriteLine($"风险分析回复: {response3Builder}");
    }
}