using MarketAssistant.Vectors.Interfaces;

namespace MarketAssistant.Vectors.Services;

/// <summary>
/// 改进的文档块读取器工厂
/// </summary>
public class DocumentBlockReaderFactory
{
    private readonly IEnumerable<IDocumentBlockReader> _readers;

    public DocumentBlockReaderFactory(IEnumerable<IDocumentBlockReader> readers)
    {
        _readers = readers;
    }

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
