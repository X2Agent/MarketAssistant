using Microsoft.Extensions.AI;

namespace MarketAssistant.Rag.Interfaces;

public interface IImageEmbeddingService
{
    /// <summary>
    /// 为图像生成浮点型嵌入向量，常用于相似度检索或 RAG（检索增强生成）场景中。
    /// 返回的向量应为规范化或可对齐到项目约定的维度。
    /// </summary>
    Task<Embedding<float>> GenerateAsync(byte[] imageBytes, CancellationToken ct = default);

    /// <summary>
    /// 可选：为图像生成简短的描述（Caption），用于索引或可访问性增强。
    /// 如果没有可用的生成器，可返回占位符或空字符串。
    /// </summary>
    Task<string> CaptionAsync(byte[] imageBytes, CancellationToken ct = default);
}
