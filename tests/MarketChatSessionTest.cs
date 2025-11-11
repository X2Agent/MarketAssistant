using MarketAssistant.Agents;
using MarketAssistant.Infrastructure;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services.Mcp;
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

        // 从 DI 容器中获取 McpService  
        var mcpService = _serviceProvider.GetRequiredService<McpService>();

        _chatSession = new MarketChatSession(chatClient, logger, mcpService);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _chatSession?.Dispose();
    }

    [TestMethod]
    public async Task TestBasicChatAsync()
    {
        // 测试基础对话功能
        var response = await _chatSession.SendMessageAsync("你好，请介绍股票投资的基础知识");

        Assert.IsNotNull(response);
        Assert.IsNotNull(response.Text);
        Assert.IsFalse(string.IsNullOrEmpty(response.Text));

        Console.WriteLine($"AI回复: {response.Text}");
    }

    [TestMethod]
    public async Task TestStockContextChatAsync()
    {
        // 设置股票上下文
        await _chatSession.UpdateStockContextAsync("sz002594");

        // 测试带有股票上下文的对话
        var response = await _chatSession.SendMessageAsync("这只股票的基本面如何？");

        Assert.IsNotNull(response);
        Assert.IsNotNull(response.Text);
        Assert.IsFalse(string.IsNullOrEmpty(response.Text));

        Console.WriteLine($"AI回复: {response.Text}");
    }

    [TestMethod]
    public async Task TestConversationHistoryAsync()
    {
        // 测试多轮对话
        await _chatSession.SendMessageAsync("什么是市盈率？");
        await _chatSession.SendMessageAsync("有何意义？");
        var response = await _chatSession.SendMessageAsync("这两个指标有什么区别");

        Assert.IsNotNull(response);
        Assert.IsNotNull(response.Text);

        // 验证对话历史
        var history = _chatSession.ConversationHistory;
        Assert.IsTrue(history.Count > 0);

        Console.WriteLine($"对话历史条数: {history.Count}");
        Console.WriteLine($"最新回复: {response.Text}");
    }

    [TestMethod]
    public void TestClearHistoryAsync()
    {
        // 添加一些对话
        _chatSession.SendMessageAsync("测试消息").Wait();

        // 验证有历史记录
        Assert.IsTrue(_chatSession.ConversationHistory.Count > 0);

        // 清除历史
        _chatSession.ClearHistory();

        // 验证历史被清空（应该只剩系统消息）
        Assert.IsTrue(_chatSession.ConversationHistory.Count <= 1);
    }

    [TestMethod]
    public async Task TestContextWindowManagementAsync()
    {
        // 设置股票上下文
        await _chatSession.UpdateStockContextAsync("sz002594");

        // 添加大量消息来测试上下文窗口管理
        for (int i = 0; i < 50; i++)
        {
            await _chatSession.SendMessageAsync($"这是第{i}次测试消息，关于sz002594的股票分析。");
        }

        // 测试对话历史是否被管理
        Assert.IsTrue(_chatSession.ConversationHistory.Count > 0);
        Assert.AreEqual("sz002594", _chatSession.CurrentStockCode);

        Console.WriteLine($"消息数: {_chatSession.ConversationHistory.Count}");
    }

    [TestMethod]
    public async Task TestTopicGuidanceAsync()
    {
        // 设置股票上下文
        await _chatSession.UpdateStockContextAsync("sz002594");

        // 询问与股票无关的消息，测试AI是否能自然地引导回相关话题
        var response = await _chatSession.SendMessageAsync("今天的天气怎么样");

        Assert.IsNotNull(response);
        Assert.IsNotNull(response.Text);
        // AI应该能够自然地回复用户或引导回股票话题

        Console.WriteLine($"AI的回复: {response.Text}");
    }

    [TestMethod]
    public async Task TestStreamingResponseAsync()
    {
        // 设置股票上下文
        await _chatSession.UpdateStockContextAsync("sz000001");

        var streamingContent = new List<string>();
        bool streamingCompleted = false;

        _chatSession.StreamingResponse += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Content))
            {
                streamingContent.Add(args.Content);
            }
            if (args.IsComplete)
            {
                streamingCompleted = true;
            }
        };

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
        Assert.IsTrue(streamingCompleted);

        var fullResponse = string.Join("", allContent);
        Console.WriteLine($"流式响应完整内容: {fullResponse}");
    }

    [TestMethod]
    public void TestCancellationAsync()
    {
        var cts = new CancellationTokenSource();

        // 启动一个可能耗时较长的请求
        var task = _chatSession.SendMessageAsync("请详细分析市场趋势", cts.Token);

        // 立即取消
        _chatSession.CancelCurrentRequest();
        cts.Cancel();

        // 验证取消状态
        Assert.IsTrue(_chatSession.IsProcessing == false);
    }

    [TestMethod]
    public async Task TestIntelligentAnalysisAsync()
    {
        // 设置股票上下文
        await _chatSession.UpdateStockContextAsync("sz000858");

        // 测试AI能否智能调用可能的插件来回答深度
        var response1 = await _chatSession.SendMessageAsync("分析MACD和RSI指标");
        Assert.IsNotNull(response1.Text);

        var response2 = await _chatSession.SendMessageAsync("这家公司的ROE和净利润如何？");
        Assert.IsNotNull(response2.Text);

        var response3 = await _chatSession.SendMessageAsync("投资这只股票有什么风险？");
        Assert.IsNotNull(response3.Text);

        Console.WriteLine($"技术分析回复: {response1.Text}");
        Console.WriteLine($"财务分析回复: {response2.Text}");
        Console.WriteLine($"风险分析回复: {response3.Text}");
    }
}
