using MarketAssistant.Rag.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace TestMarketAssistant.Vectors;

[TestClass]
public class TextChunkingServiceTest : BaseAgentTest
{
    private ITextChunkingService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        base.BaseInitialize();
        _service = _serviceProvider.GetRequiredService<ITextChunkingService>();
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Chunk_ShouldReturnSingleChunk_ForShortText()
    {
        // Arrange - 短文本（远低于 maxTokensPerParagraph=400），应聚合为单个分块
        var documentUri = "test://document";
        var input = "This is the first paragraph. It contains some text.\n\nThis is the second paragraph. It also contains some text.\n\nThis is the third paragraph. It has more text.";

        // Act
        var result = _service.Chunk(documentUri, input).ToArray();

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.Length, "短文本应聚合为单个分块");

        var chunk = result[0];
        Assert.AreEqual(documentUri, chunk.DocumentUri, "应保留原始 DocumentUri");
        Assert.AreEqual(0, chunk.Order, "首个分块 Order 应为 0");
        Assert.AreEqual("0", chunk.ParagraphId, "ParagraphId 应与 Order 一致");
        Assert.IsFalse(string.IsNullOrWhiteSpace(chunk.Text), "分块文本不应为空");
        Assert.IsFalse(string.IsNullOrEmpty(chunk.ContentHash), "应生成 ContentHash");
        Assert.IsFalse(string.IsNullOrEmpty(chunk.Key), "应生成 Key");
        Assert.AreEqual("text", chunk.SourceType, "无扩展名的 URI 应识别为 text 类型");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Chunk_ShouldSplitIntoMultipleChunks_WhenExceedingTokenLimit()
    {
        // Arrange - 构造超过 maxTokensPerParagraph(400) 的长文本，验证实际切分行为
        var documentUri = "test://document";
        var paragraphs = Enumerable.Range(0, 20).Select(i =>
            $"这是第{i}段内容，包含足够的中文文本以确保分块服务能够正常工作。" +
            $"每段都应具有独立的语义边界，便于验证分块逻辑的 token 限制约束。");
        var input = string.Join("\n\n", paragraphs);

        // Act
        var result = _service.Chunk(documentUri, input).ToArray();

        // Assert
        Assert.IsTrue(result.Length > 1, "超过 token 限制的长文本应被切分为多个分块");

        // 验证 Order 字段严格递增，证明分块顺序正确
        for (int i = 0; i < result.Length; i++)
        {
            Assert.AreEqual(i, result[i].Order, $"分块 {i} 的 Order 应为 {i}");
            Assert.AreEqual(i.ToString(), result[i].ParagraphId, $"分块 {i} 的 ParagraphId 应为 {i}");
            Assert.AreEqual(documentUri, result[i].DocumentUri, $"分块 {i} 应保留 DocumentUri");
            Assert.IsFalse(string.IsNullOrEmpty(result[i].ContentHash), $"分块 {i} 应有 ContentHash");
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Chunk_ShouldHandleEmptyString()
    {
        // Arrange
        var documentUri = "test://document";
        var input = "";

        // Act
        var result = _service.Chunk(documentUri, input).ToArray();

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Length);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Chunk_ShouldHandleNullString()
    {
        // Arrange
        var documentUri = "test://document";
        string? input = null;

        // Act
        var result = _service.Chunk(documentUri, input!).ToArray();

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Length);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Chunk_ShouldGenerateUniqueKeys()
    {
        // Arrange - 使用长文本确保产生多个分块，验证所有 Key 唯一
        var documentUri = "test://document";
        var paragraphs = Enumerable.Range(0, 20).Select(i =>
            $"这是第{i}段内容，包含足够的中文文本以确保分块服务能够正常工作。" +
            $"每段都应具有独立的语义边界，便于验证 Key 的唯一性生成逻辑。");
        var input = string.Join("\n\n", paragraphs);

        // Act
        var result = _service.Chunk(documentUri, input).ToArray();

        // Assert
        Assert.IsTrue(result.Length > 1, "应产生多个分块以验证 Key 唯一性");
        var keys = result.Select(r => r.Key).ToArray();
        Assert.AreEqual(keys.Length, keys.Distinct().Count(), "所有分块的 Key 应唯一");

        // 验证 Key 格式：{documentUriHash}:{order}:{contentHash[..8]}
        var expectedDocHashPrefix = result[0].Key.Split(':')[0];
        Assert.IsTrue(result.All(r => r.Key.StartsWith(expectedDocHashPrefix + ":", StringComparison.Ordinal)),
            "所有 Key 应以相同的 documentUri 哈希前缀开头");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Chunk_ShouldDetectSourceTypeFromUri()
    {
        // Arrange - 验证不同 URI 后缀的 SourceType 识别（核心业务规则）
        var testCases = new[]
        {
            ("doc.pdf", "pdf"),
            ("report.docx", "docx"),
            ("https://example.com/page", "web"),
            ("http://example.com/article", "web"),
            ("plain-text", "text"),
        };

        foreach (var (uri, expectedSourceType) in testCases)
        {
            // Act
            var result = _service.Chunk(uri, "测试内容用于验证 SourceType 识别").ToArray();

            // Assert
            Assert.IsTrue(result.Length > 0, $"URI '{uri}' 应至少产生一个分块");
            Assert.AreEqual(expectedSourceType, result[0].SourceType,
                $"URI '{uri}' 应识别为 '{expectedSourceType}' 类型");
        }
    }
}
