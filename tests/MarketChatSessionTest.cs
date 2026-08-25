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
        RequireLlm();
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<MarketChatSession>();
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
    [TestCategory("Integration")]
    public async Task TestBasicChatAsync()
    {
        var responseBuilder = new System.Text.StringBuilder();
        await foreach (var update in _chatSession.SendMessageStreamAsync("你好，请介绍股票投资的基础知识"))
        {
            responseBuilder.Append(update);
        }

        var responseText = responseBuilder.ToString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(responseText));
        Assert.IsTrue(responseText.Length > 20, "基础对话应返回有实质内容的回复");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task TestStockContextChatAsync()
    {
        _chatSession.SetCurrentStock("sz002594");

        var responseBuilder = new System.Text.StringBuilder();
        await foreach (var update in _chatSession.SendMessageStreamAsync("这只股票的基本面如何？"))
        {
            responseBuilder.Append(update);
        }

        var responseText = responseBuilder.ToString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(responseText));
        Assert.IsTrue(
            ContainsAnyKeyword(responseText, "002594", "比亚迪", "股票", "基本面", "估值", "财务", "盈利"),
            "回复应体现当前股票上下文");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task TestConversationHistoryAsync()
    {
        await foreach (var _ in _chatSession.SendMessageStreamAsync("什么是市盈率？")) { }
        await foreach (var _ in _chatSession.SendMessageStreamAsync("有何意义？")) { }

        var responseBuilder = new System.Text.StringBuilder();
        await foreach (var update in _chatSession.SendMessageStreamAsync("这两个指标有什么区别"))
        {
            responseBuilder.Append(update);
        }

        var responseText = responseBuilder.ToString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(responseText));

        var history = await _chatSession.GetConversationHistoryAsync();
        Assert.IsTrue(history.Count >= 6, "三轮对话应至少包含 3 条用户消息和 3 条助手回复");
        Assert.IsTrue(
            ContainsAnyKeyword(responseText, "市盈率", "PE", "估值", "盈利", "市净率", "指标", "比率"),
            "第三轮回复应延续前两轮讨论的估值指标话题");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task TestClearHistoryAsync()
    {
        await foreach (var _ in _chatSession.SendMessageStreamAsync("测试消息")) { }

        var historyBefore = await _chatSession.GetConversationHistoryAsync();
        Assert.IsTrue(historyBefore.Count > 0);

        _chatSession.ClearHistory();

        var historyAfter = await _chatSession.GetConversationHistoryAsync();
        Assert.AreEqual(0, historyAfter.Count);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task TestMultiTurnHistoryAccumulationAsync()
    {
        _chatSession.SetCurrentStock("sz002594");

        for (int i = 0; i < 10; i++)
        {
            await foreach (var _ in _chatSession.SendMessageStreamAsync($"这是第{i}次测试消息，关于sz002594的股票分析。")) { }
        }

        var history = await _chatSession.GetConversationHistoryAsync();
        Assert.IsTrue(history.Count > 0, "多轮对话后历史不应为空");
        Assert.AreEqual("sz002594", _chatSession.CurrentStockCode);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task TestTopicGuidanceAsync()
    {
        _chatSession.SetCurrentStock("sz002594");

        var responseBuilder = new System.Text.StringBuilder();
        await foreach (var update in _chatSession.SendMessageStreamAsync("今天的天气怎么样"))
        {
            responseBuilder.Append(update);
        }

        var responseText = responseBuilder.ToString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(responseText));
        Assert.IsTrue(
            ContainsAnyKeyword(responseText, "股票", "市场", "投资", "002594", "比亚迪", "标的", "分析", "金融市场"),
            "偏离话题的提问应被引导回股票或市场相关讨论");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task TestStreamingResponseAsync()
    {
        _chatSession.SetCurrentStock("sz000001");

        var allContent = new List<string>();
        await foreach (var update in _chatSession.SendMessageStreamAsync("分析sz000001的技术指标"))
        {
            if (!string.IsNullOrEmpty(update))
            {
                allContent.Add(update);
            }
        }

        Assert.IsTrue(allContent.Count > 1, "流式响应应产生多个内容块");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task TestStopCurrentRequest_OnIdleSession_IsSafeAsync()
    {
        Assert.IsFalse(_chatSession.IsProcessing, "新会话初始不应处于处理中状态");

        var history = await _chatSession.GetConversationHistoryAsync();
        Assert.AreEqual(0, history.Count, "新会话初始对话历史应为空");

        _chatSession.StopCurrentRequest();
        Assert.IsFalse(_chatSession.IsProcessing, "空闲会话调用 StopCurrentRequest 后仍不应处于处理中状态");

        _chatSession.SetCurrentStock("sz002594");
        _chatSession.StopCurrentRequest();
        Assert.AreEqual("sz002594", _chatSession.CurrentStockCode);
        Assert.IsFalse(_chatSession.IsProcessing, "设置股票上下文不应改变处理状态");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task TestIntelligentAnalysisAsync()
    {
        _chatSession.SetCurrentStock("sz002594");

        var response1Builder = new System.Text.StringBuilder();
        await foreach (var update in _chatSession.SendMessageStreamAsync("分析MACD和RSI指标"))
        {
            response1Builder.Append(update);
        }
        var response1 = response1Builder.ToString();
        Assert.IsTrue(response1.Length > 50, "技术分析回复应有足够深度");

        var response2Builder = new System.Text.StringBuilder();
        await foreach (var update in _chatSession.SendMessageStreamAsync("这家公司的ROE和净利润如何？"))
        {
            response2Builder.Append(update);
        }
        var response2 = response2Builder.ToString();
        Assert.IsTrue(response2.Length > 50, "财务分析回复应有足够深度");

        var response3Builder = new System.Text.StringBuilder();
        await foreach (var update in _chatSession.SendMessageStreamAsync("投资这只股票有什么风险？"))
        {
            response3Builder.Append(update);
        }
        var response3 = response3Builder.ToString();
        Assert.IsTrue(response3.Length > 50, "风险分析回复应有足够深度");

        var history = await _chatSession.GetConversationHistoryAsync();
        Assert.IsTrue(history.Count >= 6, "三轮分析对话应累积至少 6 条历史消息");
    }

    private static bool ContainsAnyKeyword(string text, params string[] keywords)
    {
        return keywords.Any(keyword =>
            text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
