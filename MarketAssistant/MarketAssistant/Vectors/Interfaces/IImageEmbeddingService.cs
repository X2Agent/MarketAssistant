using Microsoft.Extensions.AI;

namespace MarketAssistant.Vectors.Interfaces;

public interface IImageEmbeddingService
{
    /// <summary>
    /// 生成图像向量（与文本维度对齐或可线性映射）。
    /// </summary>
    Task<Embedding<float>> GenerateAsync(byte[] imageBytes, CancellationToken ct = default);

    /// <summary>
    /// 生成简单占位 Caption（可留空字符串）。
    /// </summary>
    Task<string> CaptionAsync(byte[] imageBytes, CancellationToken ct = default);
}
