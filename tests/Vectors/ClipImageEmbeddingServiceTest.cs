using MarketAssistant.Rag.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace TestMarketAssistant.Vectors;

/// <summary>
/// ClipImageEmbeddingService 单元测试
/// 
/// 【测试范围】：
/// 1. 图像嵌入向量生成（CLIP模型 + 哈希降级）
/// 2. 图像描述生成（多模态聊天 + 占位符降级）
/// 3. 资源管理和异常处理
/// 4. 边界条件和错误场景
/// </summary>
[TestClass]
public class ClipImageEmbeddingServiceTest : BaseKernelTest
{
    private IImageEmbeddingService _service = null!;

    // 测试用的简单图像数据（1x1像素PNG）
    private readonly byte[] _testImageBytes = new byte[]
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00,
        0x0B, 0x49, 0x44, 0x41, 0x54, 0x08, 0xD7, 0x63, 0xF8, 0x0F, 0x00, 0x00,
        0x01, 0x00, 0x01, 0x5C, 0x72, 0xA8, 0x66, 0x00, 0x00, 0x00, 0x00, 0x49,
        0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
    };

    [TestInitialize]
    public void Setup()
    {
        base.BaseInitialize();

        // 清理模型环境变量，默认走哈希降级（真实路径存在时会自动加载）
        Environment.SetEnvironmentVariable("CLIP_IMAGE_ONNX", null);

        _service = _kernel.Services.GetRequiredService<IImageEmbeddingService>();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (_service is IDisposable disposable)
        {
            disposable.Dispose();
        }
        Environment.SetEnvironmentVariable("CLIP_IMAGE_ONNX", null);
    }

    #region 图像嵌入向量生成测试

    [TestMethod]
    public async Task GenerateAsync_WithValidImage_ShouldReturnVector()
    {
        // Act
        var result = await _service.GenerateAsync(_testImageBytes);

        // Assert
        Assert.IsNotNull(result, "嵌入结果不应为null");
        Assert.IsNotNull(result.Vector, "嵌入向量不应为null");
        Assert.AreEqual(1024, result.Vector.Length, "向量维度应为1024");
    }

    [TestMethod]
    public async Task GenerateAsync_WithSameImage_ShouldReturnConsistentVector()
    {
        // Arrange
        var imageBytes = _testImageBytes;

        // Act - 两次生成同一图像的向量
        var result1 = await _service.GenerateAsync(imageBytes);
        var result2 = await _service.GenerateAsync(imageBytes);

        // Assert - 默认哈希降级应保证确定性；若加载真实模型，允许不同但维度相等
        Assert.AreEqual(result1.Vector.Length, result2.Vector.Length, "向量维度应一致");
        // 当未加载模型（走哈希降级）时，两次应完全一致
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLIP_IMAGE_ONNX")))
        {
            for (int i = 0; i < result1.Vector.Length; i++)
            {
                Assert.AreEqual(result1.Vector.Span[i], result2.Vector.Span[i]);
            }
        }
    }

    [TestMethod]
    public async Task GenerateAsync_WithInvalidImageData_ShouldUseFallback()
    {
        // Arrange - 无效的图像数据
        var invalidBytes = new byte[] { 0x00, 0x01, 0x02, 0x03 };

        // Act
        var result = await _service.GenerateAsync(invalidBytes);

        // Assert - 应该优雅处理（哈希降级或模型输出）
        Assert.IsNotNull(result, "无效图像应返回降级结果");
        Assert.IsNotNull(result.Vector, "无效图像应返回降级向量");
        Assert.AreEqual(1024, result.Vector.Length, "降级向量维度应正确");
    }

    [TestMethod]
    public async Task GenerateAsync_WithCancellation_ShouldComplete()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // 立即取消

        // Act & Assert - 哈希降级不支持取消，但应该快速完成
        var result = await _service.GenerateAsync(_testImageBytes, cts.Token);
        Assert.IsNotNull(result, "取消的操作应返回结果（哈希降级）");
    }

    #endregion

    #region 图像描述生成测试

    [TestMethod]
    public async Task GenerateCaptionAsync_WithoutChatService_ShouldReturnFallback()
    {
        // Act
        var result = await _service.CaptionAsync(_testImageBytes);

        // Assert
        Assert.AreEqual("(图像内容待解析)", result);
    }

    #endregion

    #region 资源管理和配置测试

    [TestMethod]
    public void Dispose_ShouldReleaseResources()
    {
        // Arrange
        var service = _kernel.Services.GetRequiredService<IImageEmbeddingService>();

        // Act & Assert - 应该不抛出异常
        if (service is IDisposable disposable)
        {
            disposable.Dispose();
            disposable.Dispose(); // 多次调用应该安全
        }
    }

    [TestMethod]
    public async Task MultipleOperations_ShouldWorkCorrectly()
    {
        // Arrange
        // Act - 同时进行多个操作
        var embeddingTask = _service.GenerateAsync(_testImageBytes);
        var captionTask = _service.CaptionAsync(_testImageBytes);

        var embedding = await embeddingTask;
        var caption = await captionTask;

        // Assert
        Assert.IsNotNull(embedding, "并发嵌入生成应成功");
        Assert.IsNotNull(caption, "并发描述生成应成功");
        // 默认无Chat服务，返回占位符
        Assert.AreEqual("(图像内容待解析)", caption);
    }

    #endregion
}
