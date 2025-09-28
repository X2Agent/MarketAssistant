using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;

namespace MarketAssistant.Vectors.Interfaces;

/// <summary>
/// RAG 数据摄取（清洗/分块/嵌入/写入）服务接口。
/// </summary>
public interface IRagIngestionService
{
    /// <summary>
    /// 处理并上传指定文件（支持 PDF/DOCX）。
    /// </summary>
    /// <param name="collection">目标向量集合</param>
    /// <param name="filePath">文件路径</param>
    /// <param name="embeddingGenerator">嵌入生成器</param>
    Task IngestFileAsync(VectorStoreCollection<string, TextParagraph> collection, string filePath, IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator);
}


