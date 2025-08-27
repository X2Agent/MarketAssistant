namespace MarketAssistant.Vectors.Interfaces;

/// <summary>
/// 支持同时读取文本、图片、表格块的文档读取器。
/// </summary>
public interface IDocumentBlockReader
{
    bool CanRead(string filePath);
    IEnumerable<DocumentBlock> ReadBlocks(Stream stream, string filePath);
}

public enum DocumentBlockType
{
    Text,
    Image,
    Table
}

public sealed class DocumentBlock
{
    public required DocumentBlockType Type { get; init; }

    public string? Text { get; init; }

    public byte[]? ImageBytes { get; init; }

    public int Order { get; init; }

    public string? Caption { get; set; }

    // Table specific fields
    public IReadOnlyList<IReadOnlyList<string>>? TableRows { get; init; }

    public string? TableMarkdown { get; init; }

    public string? TableCaption { get; init; }

    public string? TableHash { get; init; }
}
