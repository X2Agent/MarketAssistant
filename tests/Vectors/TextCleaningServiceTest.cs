using MarketAssistant.Rag.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace TestMarketAssistant.Vectors;

[TestClass]
public class TextCleaningServiceTest : BaseAgentTest
{
    private ITextCleaningService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        base.BaseInitialize();
        _service = _serviceProvider.GetRequiredService<ITextCleaningService>();
    }

    [TestMethod]
    public void Clean_ShouldRemoveExtraWhitespace()
    {
        // Arrange
        var input = "This   is  a   test  string";
        var expected = "This is a test string";

        // Act
        var result = _service.Clean(input);

        // Assert
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void Clean_ShouldRemoveLeadingAndTrailingWhitespace()
    {
        // Arrange
        var input = "   This is a test string   ";
        var expected = "This is a test string";

        // Act
        var result = _service.Clean(input);

        // Assert
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void Clean_ShouldHandleEmptyString()
    {
        // Arrange
        var input = "";
        var expected = "";

        // Act
        var result = _service.Clean(input);

        // Assert
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void Clean_ShouldHandleNullString()
    {
        // Arrange
        string? input = null;
        var expected = "";

        // Act
        var result = _service.Clean(input);

        // Assert
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void Clean_ShouldRemovePageNumbers()
    {
        // Arrange
        var input = "This is a test string. Page 1 of 10";
        var expected = "This is a test string.";

        // Act
        var result = _service.Clean(input);

        // Assert
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void Clean_ShouldRemoveUrls()
    {
        // Arrange
        var input = "This is a test string with a URL: https://example.com";
        var expected = "This is a test string with a URL:";

        // Act
        var result = _service.Clean(input);

        // Assert
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void Clean_ShouldNormalizeLineEndings()
    {
        // Arrange
        var input = "Line 1\r\nLine 2\rLine 3";
        var expected = "Line 1\nLine 2\nLine 3";

        // Act
        var result = _service.Clean(input);

        // Assert
        Assert.AreEqual(expected, result);
    }
}