using MarketAssistant.Services.Trading;

namespace TestMarketAssistant.Trading;

/// <summary>
/// AI 结构化决策解析器测试：验证从 LLM 原始响应中稳健提取 JSON 决策，
/// 容忍 markdown 包裹、多余文本、属性别名与字符串内大括号。
/// </summary>
[TestClass]
public sealed class AISignalDecisionParserTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public void PlainJson_IsFullyParsed()
    {
        var ok = AISignalDecisionParser.TryParse(
            """{"decision":"BUY","confidence":80,"stopLossPrice":95.5,"takeProfitPrice":120,"reason":"突破放量"}""",
            out var decision);

        Assert.IsTrue(ok);
        Assert.IsTrue(decision!.IsBuy);
        Assert.AreEqual(80, decision.Confidence);
        Assert.AreEqual(95.5m, decision.StopLossPrice);
        Assert.AreEqual(120m, decision.TakeProfitPrice);
        Assert.AreEqual("突破放量", decision.Reason);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void MarkdownFencedJson_IsExtracted()
    {
        var response = """
            根据分析，我的决策如下：

            ```json
            {"decision": "SELL", "confidence": 70, "stopLossPrice": null, "takeProfitPrice": 90}
            ```

            以上仅供参考。
            """;

        var ok = AISignalDecisionParser.TryParse(response, out var decision);

        Assert.IsTrue(ok);
        Assert.IsTrue(decision!.IsSell);
        Assert.AreEqual(70, decision.Confidence);
        Assert.IsNull(decision.StopLossPrice);
        Assert.AreEqual(90m, decision.TakeProfitPrice);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ActionAlias_IsAccepted_AndNormalized()
    {
        var ok = AISignalDecisionParser.TryParse(
            """{"action": "hold", "confidence": 30}""", out var decision);

        Assert.IsTrue(ok);
        Assert.IsTrue(decision!.IsHold);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void BracesInsideStringReason_AreHandled()
    {
        var ok = AISignalDecisionParser.TryParse(
            """{"decision":"HOLD","confidence":40,"reason":"价格 {快速} 回落，观望 {更稳妥}"}""",
            out var decision);

        Assert.IsTrue(ok);
        Assert.IsTrue(decision!.IsHold);
        Assert.IsTrue(decision.Reason!.Contains("{快速}"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ConfidenceOutOfRange_IsClampedTo100()
    {
        var ok = AISignalDecisionParser.TryParse(
            """{"decision":"BUY","confidence":150}""", out var decision);

        Assert.IsTrue(ok);
        Assert.AreEqual(100, decision!.Confidence);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void StringPrice_IsAccepted()
    {
        var ok = AISignalDecisionParser.TryParse(
            """{"decision":"BUY","confidence":75,"stopLossPrice":"95.5"}""", out var decision);

        Assert.IsTrue(ok);
        Assert.AreEqual(95.5m, decision!.StopLossPrice);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void NonJsonText_ReturnsFalse()
    {
        Assert.IsFalse(AISignalDecisionParser.TryParse("我认为应该买入 BTC", out _));
        Assert.IsFalse(AISignalDecisionParser.TryParse(null, out _));
        Assert.IsFalse(AISignalDecisionParser.TryParse(string.Empty, out _));
        Assert.IsFalse(AISignalDecisionParser.TryParse("""{"decision": broken}""", out _));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void NonPositivePrices_AreTreatedAsNull()
    {
        var ok = AISignalDecisionParser.TryParse(
            """{"decision":"BUY","confidence":75,"stopLossPrice":0,"takeProfitPrice":-5}""",
            out var decision);

        Assert.IsTrue(ok);
        Assert.IsNull(decision!.StopLossPrice);
        Assert.IsNull(decision.TakeProfitPrice);
    }
}