using MarketAssistant.Vectors.Interfaces;

namespace MarketAssistant.Vectors.Services;

/// <summary>
/// PDF 文档块读取器：支持结构化块读取
/// 使用适配器模式，内部委托给 MarkdownDocumentBlockReader 处理
/// </summary>
public class PdfBlockReader : IDocumentBlockReader
{
    private readonly MarkdownDocumentBlockReader _markdownReader;

    public PdfBlockReader(MarkdownDocumentBlockReader markdownReader)
    {
        _markdownReader = markdownReader ?? throw new ArgumentNullException(nameof(markdownReader));
    }

    public bool CanRead(string filePath) =>
        filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    public async Task<IEnumerable<DocumentBlock>> ReadBlocksAsync(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        // 直接委托给 MarkdownDocumentBlockReader 处理
        return await _markdownReader.ReadBlocksAsync(filePath);
    }
}
