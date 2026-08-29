namespace MarketAssistant.Rag.Interfaces;

/// <summary>
/// 支持从文档中读取文本、图片、表格等块级内容的抽象接口。
/// 设计目标是将复杂文档（PDF、Markdown、Word 等）拆分为可索引的内容块。
/// </summary>
public interface IDocumentBlockReader
{
    bool CanRead(string filePath);

    /// <param name="cancellationToken">取消令牌：文档解析（含 PDF/DOCX 解码）阶段可取消</param>
    Task<IEnumerable<DocumentBlock>> ReadBlocksAsync(string filePath, CancellationToken cancellationToken = default);
}
