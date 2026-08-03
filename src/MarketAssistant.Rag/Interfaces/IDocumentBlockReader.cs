namespace MarketAssistant.Rag.Interfaces;

/// <summary>
/// 支持从文档中读取文本、图片、表格等块级内容的抽象接口。
/// 设计目标是将复杂文档（PDF、Markdown、Word 等）拆分为可索引的内容块。
/// </summary>
public interface IDocumentBlockReader
{
    bool CanRead(string filePath);
    Task<IEnumerable<DocumentBlock>> ReadBlocksAsync(string filePath);
}
