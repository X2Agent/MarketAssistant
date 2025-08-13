using MarketAssistant.Vectors.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;

namespace TestMarketAssistant.Vectors;

[TestClass]
public class TextChunkingServiceTest
{
    [TestMethod]
    public void Chunk_ShouldSplitTextIntoParagraphs()
    {
        // Arrange
        var service = new TextChunkingService();
        var documentUri = "test://document";
        var input = "This is the first paragraph. It contains some text.\n\nThis is the second paragraph. It also contains some text.\n\nThis is the third paragraph. It has more text.";

        // Act
        var result = service.Chunk(documentUri, input).ToArray();

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
        var service = new TextChunkingService();
        var documentUri = "test://document";
        var input = "";

        // Act
        var result = service.Chunk(documentUri, input).ToArray();

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Length);
    }

    [TestMethod]
    public void Chunk_ShouldHandleNullString()
    {
        // Arrange
        var service = new TextChunkingService();
        var documentUri = "test://document";
        string? input = null;

        // Act
        var result = service.Chunk(documentUri, input!).ToArray();

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Length);
    }

    [TestMethod]
    public void Chunk_ShouldGenerateUniqueKeys()
    {
        // Arrange
        var service = new TextChunkingService();
        var documentUri = "test://document";
        var input = "This is the first paragraph. It contains some text.\n\nThis is the second paragraph. It also contains some text.";

        // Act
        var result = service.Chunk(documentUri, input).ToArray();

        // Assert
        Assert.IsNotNull(result);
        if (result.Length > 1)
        {
            Assert.AreNotEqual(result[0].Key, result[1].Key);
        }
    }
}