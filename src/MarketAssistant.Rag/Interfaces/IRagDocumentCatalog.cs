namespace MarketAssistant.Rag.Interfaces;

/// <summary>
/// 已摄取文档的清单条目（P1-01）。
/// </summary>
/// <param name="CollectionName">所属向量集合。</param>
/// <param name="DocumentId">稳定文档标识（<see cref="RagDocumentId"/>）。</param>
/// <param name="DocumentUri">原始文件路径或 URI。</param>
/// <param name="ContentHash">文档内容哈希（用于幂等判断）。</param>
/// <param name="Keys">当前在库的全部段落 Key。</param>
/// <param name="EmbeddingModelId">生成向量时的嵌入模型标识。</param>
/// <param name="Dimension">嵌入维度。</param>
/// <param name="UpdatedAt">最近更新时间（UTC）。</param>
public sealed record RagDocumentCatalogEntry(
    string CollectionName,
    string DocumentId,
    string DocumentUri,
    string ContentHash,
    IReadOnlyList<string> Keys,
    string EmbeddingModelId,
    int Dimension,
    DateTimeOffset UpdatedAt);

/// <summary>
/// 文档段落清单（P1-01）：记录每个文档当前在库的 Key 集合，
/// 支持文档级替换（删除旧 Key）与删除文档语义。持久化使用 SQLite 旁路表。
/// </summary>
public interface IRagDocumentCatalog
{
    /// <summary>
    /// 获取指定文档当前在库的全部 Key；无记录时返回空列表。
    /// </summary>
    Task<IReadOnlyList<string>> GetKeysAsync(
        string collectionName, string documentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 写入/覆盖文档清单（文档级替换的最后一步：新段落全部写入成功后调用）。
    /// </summary>
    Task ReplaceAsync(RagDocumentCatalogEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除文档清单记录（应先按 <see cref="GetKeysAsync"/> 删除向量库中的段落）。
    /// </summary>
    Task RemoveAsync(string collectionName, string documentId, CancellationToken cancellationToken = default);
}