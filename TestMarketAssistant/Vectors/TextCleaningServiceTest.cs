using MarketAssistant.Vectors.Services;

namespace TestMarketAssistant.Vectors;

[TestClass]
public class TextCleaningServiceTest
{
    [TestMethod]
    public void Clean_ShouldRemoveExtraWhitespace()
    {
        // Arrange
        var service = new TextCleaningService();
        var input = "This   is  a   test  string";
        var expected = "This is a test string";

        // Act
        var result = service.Clean(input);

        // Assert
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void Clean_ShouldRemoveLeadingAndTrailingWhitespace()
    {
        // Arrange
        var service = new TextCleaningService();
        var input = "   This is a test string   ";
        var expected = "This is a test string";

        // Act
        var result = service.Clean(input);

        // Assert
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void Clean_ShouldHandleEmptyString()
    {
        // Arrange
        var service = new TextCleaningService();
        var input = "";
        var expected = "";

        // Act
        var result = service.Clean(input);

        // Assert
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void Clean_ShouldHandleNullString()
    {
        // Arrange
        var service = new TextCleaningService();
        string? input = null;
        var expected = "";

        // Act
        var result = service.Clean(input);

        // Assert
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void Clean_ShouldRemovePageNumbers()
    {
        // Arrange
        var service = new TextCleaningService();
        var input = "This is a test string. Page 1 of 10";
        var expected = "This is a test string.";

        // Act
        var result = service.Clean(input);

        // Assert
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void Clean_ShouldRemoveUrls()
    {
        // Arrange
        var service = new TextCleaningService();
        var input = "This is a test string with a URL: https://example.com";
        var expected = "This is a test string with a URL:";

        // Act
        var result = service.Clean(input);

        // Assert
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void Clean_ShouldNormalizeLineEndings()
    {
        // Arrange
        var service = new TextCleaningService();
        var input = "Line 1\r\nLine 2\rLine 3";
        var expected = "Line 1\nLine 2\nLine 3";

        // Act
        var result = service.Clean(input);

        // Assert
        Assert.AreEqual(expected, result);
    }
}