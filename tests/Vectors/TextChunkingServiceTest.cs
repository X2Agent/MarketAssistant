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
    public void Chunk_ShouldSplitTextIntoParagraphs()
    {
        // Arrange
        var documentUri = "test://document";
        var input = "This is the first paragraph. It contains some text.\n\nThis is the second paragraph. It also contains some text.\n\nThis is the third paragraph. It has more text.";

        // Act
        var result = _service.Chunk(documentUri, input).ToArray();

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Length > 0);
        Assert.AreEqual(documentUri, result[0].DocumentUri);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result[0].Text));
    }

    [TestMethod]
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
    public void Chunk_ShouldGenerateUniqueKeys()
    {
        // Arrange
        var documentUri = "test://document";
        var input = "This is the first paragraph. It contains some text.\n\nThis is the second paragraph. It also contains some text.";

        // Act
        var result = _service.Chunk(documentUri, input).ToArray();

        // Assert
        Assert.IsNotNull(result);
        if (result.Length > 1)
        {
            Assert.AreNotEqual(result[0].Key, result[1].Key);
        }
    }
}