using MarketAssistant.Rag;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;

namespace MarketAssistant.Rag.Interfaces;

/// <summary>
/// 单个块摄取失败信息。
/// </summary>
/// <param name="BlockOrder">失败块在文档中的顺序。</param>
/// <param name="ErrorCode">错误码，用于区分失败类别（如 EmbeddingDimensionMismatch、EmbeddingCountMismatch、BlockProcessingError）。</param>
/// <param name="Message">面向用户的错误说明。</param>
public sealed record RagIngestionFailure(
    int BlockOrder,
    string ErrorCode,
    string Message);

/// <summary>
/// 文件摄取结果：区分完全成功、部分成功与失败，避免把部分块失败报告为文件成功。
/// </summary>
public sealed record RagIngestionResult(
    int BlockCount,
    int ParagraphCount,
    IReadOnlyList<RagIngestionFailure> Failures)
{
    /// <summary>全部块成功入库。</summary>
    public bool IsSuccess => Failures.Count == 0 && ParagraphCount > 0;

    /// <summary>部分块成功入库，存在失败块。</summary>
    public bool IsPartialSuccess => ParagraphCount > 0 && Failures.Count > 0;

    /// <summary>没有任何段落入库。</summary>
    public bool IsFailure => ParagraphCount == 0;
}

/// <summary>
/// RAG 数据摄取（清洗/分块/嵌入/写入）服务接口。
/// </summary>
public interface IRagIngestionService
{
    /// <summary>
    /// 处理并上传指定文件（支持 PDF/DOCX），返回结构化摄取结果。
    /// </summary>
    /// <param name="collection">目标向量集合</param>
    /// <param name="collectionName">集合名称（清单按集合隔离）</param>
    /// <param name="filePath">文件路径</param>
    /// <param name="embeddingGenerator">嵌入生成器</param>
    /// <param name="cancellationToken">取消令牌；取消以 <see cref="OperationCanceledException"/> 抛出，不作为失败计入结果。</param>
    Task<RagIngestionResult> IngestFileAsync(
        VectorStoreCollection<string, TextParagraph> collection,
        string collectionName,
        string filePath,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        CancellationToken cancellationToken = default);
}


