using MarketAssistant.Rag.Interfaces;

namespace MarketAssistant.Rag.Services;

/// <summary>
/// 改进的文档块读取器工厂
/// </summary>
public class DocumentBlockReaderFactory(IEnumerable<IDocumentBlockReader> readers)
{
    private readonly IEnumerable<IDocumentBlockReader> _readers = readers;

    public IDocumentBlockReader? GetReader(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return null;
        }

        return _readers.FirstOrDefault(c => c.CanRead(filePath));
    }

    public bool CanRead(string filePath)
    {
        return GetReader(filePath) != null;
    }
}
