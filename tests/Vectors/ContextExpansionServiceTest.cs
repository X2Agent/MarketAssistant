using MarketAssistant.Rag;
using MarketAssistant.Rag.Services;

namespace TestMarketAssistant.Vectors;

/// <summary>
/// P1-02：邻接上下文扩展——同文档 Order±window 拼接【上文】/【下文】。
/// </summary>
[TestClass]
public class ContextExpansionServiceTest
{
    private static RagSearchCandidate Candidate(string key, string uri, int order, string text)
        => new(new TextParagraph
        {
            Key = key,
            DocumentUri = uri,
            ParagraphId = key,
            Text = text,
            Order = order,
            SourceType = "test"
        }, 0.5, string.Empty);

    [TestMethod]
    [TestCategory("Unit")]
    public void BuildExpandedText_ShouldPrependPreviousAndAppendNext()
    {
        var service = new ContextExpansionService();
        var pool = new List<RagSearchCandidate>
        {
            Candidate("d:p0", "doc://a", 0, "第一段"),
            Candidate("d:p1", "doc://a", 1, "第二段"),
            Candidate("d:p2", "doc://a", 2, "第三段"),
            Candidate("d:p3", "doc://b", 3, "另一文档"), // 不同文档不参与
        };

        var selected = pool[1];
        var expanded = service.BuildExpandedText(selected, pool);

        StringAssert.Contains(expanded, "【上文】第一段");
        StringAssert.Contains(expanded, "第二段");
        StringAssert.Contains(expanded, "【下文】第三段");
        Assert.IsFalse(expanded.Contains("另一文档"), "不同文档不应参与扩展");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void BuildExpandedText_NoNeighbors_ShouldReturnOriginalText()
    {
        var service = new ContextExpansionService();
        var selected = Candidate("only", "doc://x", 5, "孤立段落");

        var expanded = service.BuildExpandedText(selected, [selected]);

        Assert.AreEqual("孤立段落", expanded);
    }
}