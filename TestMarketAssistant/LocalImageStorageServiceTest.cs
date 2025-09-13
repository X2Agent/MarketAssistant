using MarketAssistant.Vectors.Services;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace TestMarketAssistant;

[TestClass]
public class LocalImageStorageServiceTest
{
    private LocalImageStorageService _service = null!;
    private string _testDirectory = null!;
    private const string TestDocumentPath = "test-document.pdf";

    [TestInitialize]
    public void Initialize()
    {
        _service = new LocalImageStorageService();
        _testDirectory = Path.Combine(Path.GetTempPath(), "LocalImageStorageServiceTest", Guid.NewGuid().ToString());
    }

    [TestCleanup]
    public void Cleanup()
    {
        // 清理测试目录
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [TestMethod]
    public async Task SaveImageAsync_WithValidImageAndHint_ShouldSaveSuccessfully()
    {
        // Arrange
        var imageBytes = CreateTestImageBytes();
        var fileNameHint = "test-image.png";

        // Act
        var result = await _service.SaveImageAsync(imageBytes, fileNameHint, TestDocumentPath);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(File.Exists(result));
        Assert.IsTrue(result.Contains("test-image"));
        Assert.IsTrue(result.EndsWith(".png"));
    }

    [TestMethod]
    public async Task SaveImageAsync_WithEmptyHint_ShouldGenerateGuidName()
    {
        // Arrange
        var imageBytes = CreateTestImageBytes();

        // Act
        var result = await _service.SaveImageAsync(imageBytes, "", TestDocumentPath);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(File.Exists(result));
        Assert.IsTrue(result.EndsWith(".png"));
    }

    [TestMethod]
    public async Task SaveImageAsync_WithNullHint_ShouldGenerateGuidName()
    {
        // Arrange
        var imageBytes = CreateTestImageBytes();

        // Act
        var result = await _service.SaveImageAsync(imageBytes, null!, TestDocumentPath);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(File.Exists(result));
        Assert.IsTrue(result.EndsWith(".png"));
    }

    [TestMethod]
    public async Task SaveImageAsync_WithDuplicateFileName_ShouldOverwriteExistingFile()
    {
        // Arrange
        var imageBytes1 = CreateTestImageBytes();
        var imageBytes2 = new byte[] { 1, 2, 3, 4 }; // Different content
        var fileNameHint = "duplicate.png";

        // Act
        var result1 = await _service.SaveImageAsync(imageBytes1, fileNameHint, TestDocumentPath);
        var result2 = await _service.SaveImageAsync(imageBytes2, fileNameHint, TestDocumentPath);

        // Assert
        Assert.IsNotNull(result1);
        Assert.IsNotNull(result2);
        Assert.AreEqual(result1, result2); // Should be the same path (overwritten)
        Assert.IsTrue(File.Exists(result1));
        Assert.IsTrue(File.Exists(result2));
        
        // Verify the file content matches the second save (overwritten)
        var savedBytes = await File.ReadAllBytesAsync(result2);
        CollectionAssert.AreEqual(imageBytes2, savedBytes);
    }

    [TestMethod]
    public async Task SaveImageAsync_WithUnsafeFileName_ShouldCleanFileName()
    {
        // Arrange
        var imageBytes = CreateTestImageBytes();
        var unsafeHint = "unsafe<>:|?*.png";

        // Act
        var result = await _service.SaveImageAsync(imageBytes, unsafeHint, TestDocumentPath);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(File.Exists(result));
        var fileName = Path.GetFileName(result);
        Assert.IsFalse(fileName.Contains('<'));
        Assert.IsFalse(fileName.Contains('>'));
        Assert.IsFalse(fileName.Contains(':'));
        Assert.IsFalse(fileName.Contains('|'));
        Assert.IsFalse(fileName.Contains('?'));
        Assert.IsFalse(fileName.Contains('*'));
    }

    [TestMethod]
    public async Task SaveImageAsync_WithUnsupportedExtension_ShouldUsePngExtension()
    {
        // Arrange
        var imageBytes = CreateTestImageBytes();
        var fileNameHint = "test.xyz";

        // Act
        var result = await _service.SaveImageAsync(imageBytes, fileNameHint, TestDocumentPath);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.EndsWith(".png"));
        Assert.IsTrue(File.Exists(result));
    }

    [TestMethod]
    public async Task SaveImageAsync_WithLongFileName_ShouldTruncate()
    {
        // Arrange
        var imageBytes = CreateTestImageBytes();
        var longName = new string('a', 300) + ".png";

        // Act
        var result = await _service.SaveImageAsync(imageBytes, longName, TestDocumentPath);

        // Assert
        Assert.IsNotNull(result);
        var fileName = Path.GetFileName(result);
        Assert.IsTrue(fileName.Length <= 255);
        Assert.IsTrue(File.Exists(result));
    }

    [TestMethod]
    public async Task SaveImageAsync_WithNullImageBytes_ShouldThrowArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsExceptionAsync<ArgumentException>(
            () => _service.SaveImageAsync(null!, "test.png", TestDocumentPath));
    }

    [TestMethod]
    public async Task SaveImageAsync_WithEmptyImageBytes_ShouldThrowArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsExceptionAsync<ArgumentException>(
            () => _service.SaveImageAsync(Array.Empty<byte>(), "test.png", TestDocumentPath));
    }

    [TestMethod]
    public async Task SaveImageAsync_WithTooLargeImage_ShouldThrowArgumentException()
    {
        // Arrange
        var largeImageBytes = new byte[11 * 1024 * 1024]; // 11MB

        // Act & Assert
        await Assert.ThrowsExceptionAsync<ArgumentException>(
            () => _service.SaveImageAsync(largeImageBytes, "large.png", TestDocumentPath));
    }

    [TestMethod]
    public async Task SaveImageAsync_WithNullDocumentPath_ShouldThrowArgumentException()
    {
        // Arrange
        var imageBytes = CreateTestImageBytes();

        // Act & Assert
        await Assert.ThrowsExceptionAsync<ArgumentException>(
            () => _service.SaveImageAsync(imageBytes, "test.png", null!));
    }

    [TestMethod]
    public async Task SaveImageAsync_WithEmptyDocumentPath_ShouldThrowArgumentException()
    {
        // Arrange
        var imageBytes = CreateTestImageBytes();

        // Act & Assert
        await Assert.ThrowsExceptionAsync<ArgumentException>(
            () => _service.SaveImageAsync(imageBytes, "test.png", ""));
    }

    [TestMethod]
    public async Task SaveImageAsync_WithWhitespaceDocumentPath_ShouldThrowArgumentException()
    {
        // Arrange
        var imageBytes = CreateTestImageBytes();

        // Act & Assert
        await Assert.ThrowsExceptionAsync<ArgumentException>(
            () => _service.SaveImageAsync(imageBytes, "test.png", "   "));
    }

    [TestMethod]
    public async Task SaveImageAsync_CreatesDirectoryStructure()
    {
        // Arrange
        var imageBytes = CreateTestImageBytes();
        var documentPath = Path.Combine("subfolder", "deep", "document.pdf");

        // Act
        var result = await _service.SaveImageAsync(imageBytes, "test.png", documentPath);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(File.Exists(result));
        
        // 验证目录结构
        var imageDir = Path.GetDirectoryName(result);
        Assert.IsNotNull(imageDir);
        Assert.IsTrue(Directory.Exists(imageDir));
    }

    [TestMethod]
    public async Task SaveImageAsync_WithCancellationToken_ShouldRespectCancellation()
    {
        // Arrange
        var imageBytes = CreateTestImageBytes();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(
            () => _service.SaveImageAsync(imageBytes, "test.png", TestDocumentPath, cts.Token));
    }

    private static byte[] CreateTestImageBytes()
    {
        // 创建一个简单的测试图像数据（PNG格式的最小头部）
        return [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0, 144, 119, 83, 222, 0, 0, 0, 12, 73, 68, 65, 84, 8, 215, 99, 248, 15, 0, 0, 1, 0, 1, 78, 117, 24, 230, 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130];
    }
}
