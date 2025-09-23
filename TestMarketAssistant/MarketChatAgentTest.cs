using MarketAssistant.Agents;
using MarketAssistant.Infrastructure;
using Microsoft.SemanticKernel.ChatCompletion;

namespace TestMarketAssistant;

[TestClass]
public class MarketChatAgentTest : BaseKernelTest
{
    private MarketChatAgent _chatAgent;
    private MarketAnalysisAgent _analysisAgent;

    [TestInitialize]
    public void Initialize()
    {
        BaseInitialize();

        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<MarketChatAgent>();
        var analysisLogger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<MarketAnalysisAgent>();
        var analystManagerLogger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<AnalystManager>();
        var kernelPluginConfig = _kernel.Services.GetRequiredService<IKernelPluginConfig>();
        var analystManager = new AnalystManager(_kernel, analystManagerLogger, _userSettingService, kernelPluginConfig);

        _analysisAgent = new MarketAnalysisAgent(analysisLogger, analystManager);
        _chatAgent = new MarketChatAgent(logger, _kernel, _analysisAgent);
    }

    [TestMethod]
    public async Task TestBasicChatAsync()
    {
        // 测试基本对话功能
        var response = await _chatAgent.SendMessageAsync("你好，我想了解股票投资的基本知识");

        Assert.IsNotNull(response);
        Assert.IsNotNull(response.Content);
        Assert.IsFalse(string.IsNullOrEmpty(response.Content));

        Console.WriteLine($"AI回复: {response.Content}");
    }

    [TestMethod]
    public async Task TestStockContextChatAsync()
    {
        // 设置股票上下文
        await _chatAgent.UpdateStockContextAsync("sz002594");

        // 测试带股票上下文的对话
        var response = await _chatAgent.SendMessageAsync("这只股票的基本面如何？");

        Assert.IsNotNull(response);
        Assert.IsNotNull(response.Content);
        Assert.IsFalse(string.IsNullOrEmpty(response.Content));

        Console.WriteLine($"AI回复: {response.Content}");
    }

    [TestMethod]
    public async Task TestConversationHistoryAsync()
    {
        // 测试多轮对话
        await _chatAgent.SendMessageAsync("什么是市盈率？");
        await _chatAgent.SendMessageAsync("那市净率呢？");
        var response = await _chatAgent.SendMessageAsync("这两个指标有什么区别？");

        Assert.IsNotNull(response);
        Assert.IsNotNull(response.Content);

        // 验证对话历史
        var history = _chatAgent.ConversationHistory;
        Assert.IsTrue(history.Count > 0);

        Console.WriteLine($"对话历史条数: {history.Count}");
        Console.WriteLine($"最新回复: {response.Content}");
    }

    [TestMethod]
    public void TestClearHistoryAsync()
    {
        // 添加一些对话
        _chatAgent.SendMessageAsync("测试消息").Wait();

        // 验证有历史记录
        Assert.IsTrue(_chatAgent.ConversationHistory.Count > 0);

        // 清空历史
        _chatAgent.ClearHistory();

        // 验证历史已清空（应该只剩系统消息）
        Assert.IsTrue(_chatAgent.ConversationHistory.Count <= 1);
    }

    [TestMethod]
    public async Task TestContextWindowManagementAsync()
    {
        // 设置股票上下文
        await _chatAgent.UpdateStockContextAsync("sz002594");

        // 添加大量消息来测试上下文窗口管理
        for (int i = 0; i < 50; i++)
        {
            await _chatAgent.SendMessageAsync($"这是第{i}条测试消息，关于sz002594的股票分析。");
        }

        // 检查上下文统计
        var stats = _chatAgent.GetContextStatistics();
        Assert.IsTrue(stats.TotalMessages > 0);
        Assert.AreEqual("sz002594", stats.CurrentStockCode);

        Console.WriteLine($"消息数: {stats.TotalMessages}, 利用率: {stats.MessageUtilization:P2}");
    }

    [TestMethod]
    public async Task TestTopicGuidanceAsync()
    {
        // 设置股票上下文
        await _chatAgent.UpdateStockContextAsync("sz002594");

        // 发送与股票无关的消息，测试AI是否能自然引导回主题
        var response = await _chatAgent.SendMessageAsync("今天天气怎么样？");

        Assert.IsNotNull(response);
        Assert.IsNotNull(response.Content);
        // AI应该能够自然地引导用户回到股票话题
        
        Console.WriteLine($"AI引导回复: {response.Content}");
    }

    [TestMethod]
    public async Task TestStreamingResponseAsync()
    {
        // 设置股票上下文
        await _chatAgent.UpdateStockContextAsync("sz000001");

        var streamingContent = new List<string>();
        bool streamingCompleted = false;

        _chatAgent.StreamingResponse += (sender, args) =>
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
        await foreach (var chunk in _chatAgent.SendMessageStreamAsync("请分析sz000001的技术指标"))
        {
            if (!string.IsNullOrEmpty(chunk.Content))
            {
                allContent.Add(chunk.Content);
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

        // 启动一个长时间运行的任务
        var task = _chatAgent.SendMessageAsync("请详细分析市场趋势", cts.Token);

        // 立即取消
        _chatAgent.CancelCurrentRequest();
        cts.Cancel();

        // 验证任务状态
        Assert.IsTrue(_chatAgent.IsProcessing == false);
    }

    [TestMethod]
    public async Task TestIntelligentAnalysisAsync()
    {
        // 设置股票上下文
        await _chatAgent.UpdateStockContextAsync("sz000858");

        // 测试AI能否根据问题类型智能调整分析角度
        var response1 = await _chatAgent.SendMessageAsync("请分析MACD和RSI指标");
        Assert.IsNotNull(response1.Content);

        var response2 = await _chatAgent.SendMessageAsync("这家公司的ROE和市盈率如何？");
        Assert.IsNotNull(response2.Content);

        var response3 = await _chatAgent.SendMessageAsync("投资这只股票有什么风险？");
        Assert.IsNotNull(response3.Content);

        Console.WriteLine($"技术分析回复: {response1.Content}");
        Console.WriteLine($"基本面分析回复: {response2.Content}");
        Console.WriteLine($"风险分析回复: {response3.Content}");
    }
}