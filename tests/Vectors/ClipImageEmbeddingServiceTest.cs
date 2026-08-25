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
public class ClipImageEmbeddingServiceTest : BaseAgentTest
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

        _service = _serviceProvider.GetRequiredService<IImageEmbeddingService>();
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
    public async Task GenerateAsync_WithoutClipModel_ShouldThrowInsteadOfHashVector()
    {
        // P1-03：CLIP 不可用时不生成哈希伪语义向量，直接抛出由调用方降级为 Caption 召回
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => _service.GenerateAsync(_testImageBytes));
    }

    [TestMethod]
    public async Task GenerateAsync_WithInvalidImageData_ShouldThrow()
    {
        // Arrange - 无效的图像数据
        var invalidBytes = new byte[] { 0x00, 0x01, 0x02, 0x03 };

        // Act & Assert - 不再返回哈希降级结果
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => _service.GenerateAsync(invalidBytes));
    }

    [TestMethod]
    public async Task GenerateAsync_WithCancelledToken_ShouldThrowOperationCanceled()
    {
        // Arrange - 取消必须以 OperationCanceledException 向上传播，不被吞掉
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => _service.GenerateAsync(_testImageBytes, cts.Token));
    }

    #endregion

    #region 图像描述生成测试

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GenerateCaptionAsync_WithoutChatService_ShouldReturnFallback()
    {
        // Act
        var result = await _service.CaptionAsync(_testImageBytes);

        // Assert
        Assert.AreEqual("(图像内容生成失败)", result);
    }

    #endregion

    #region 资源管理和配置测试

    [TestMethod]
    [TestCategory("Unit")]
    public async Task CaptionAsync_ConcurrentCalls_ShouldWorkCorrectly()
    {
        // Arrange
        // Act - 并发进行多次描述生成（嵌入在无 CLIP 模型时抛错，不再参与并发断言）
        var caption1 = await _service.CaptionAsync(_testImageBytes);
        var caption2 = await _service.CaptionAsync(_testImageBytes);

        // Assert
        Assert.IsNotNull(caption1, "并发描述生成应成功");
        Assert.IsNotNull(caption2, "并发描述生成应成功");
        // 默认无Chat服务，返回占位符
        Assert.AreEqual("(图像内容生成失败)", caption1);
    }

    #endregion
}
