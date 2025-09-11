namespace MarketAssistant.Vectors.Interfaces;

/// <summary>
/// 支持同时读取文本、图片、表格块的文档读取器。
/// </summary>
public interface IDocumentBlockReader
{
    bool CanRead(string filePath);
    Task<IEnumerable<DocumentBlock>> ReadBlocksAsync(string filePath);
}

public enum DocumentBlockType
{
    /// <summary>
    /// 普通文本段落
    /// </summary>
    Text,

    /// <summary>
    /// 标题（不同级别的标题有不同的语义重要性）
    /// </summary>
    Heading,

    /// <summary>
    /// 列表项（有序/无序列表）
    /// </summary>
    List,

    /// <summary>
    /// 表格数据
    /// </summary>
    Table,

    /// <summary>
    /// 图片/图表
    /// </summary>
    Image
}

/// <summary>
/// 文档块基类
/// </summary>
public abstract class DocumentBlock
{
    public DocumentBlockType Type { get; init; }
    public required int Order { get; init; }
    public string? Caption { get; set; }
}

/// <summary>
/// 文本块
/// </summary>
public sealed class TextBlock : DocumentBlock
{
    public required string Text { get; init; }

    public TextBlock()
    {
        Type = DocumentBlockType.Text;
    }
}

/// <summary>
/// 标题块
/// </summary>
public sealed class HeadingBlock : DocumentBlock
{
    public required string Text { get; init; }
    public required int Level { get; init; } // 1-6，对应H1-H6

    public HeadingBlock()
    {
        Type = DocumentBlockType.Heading;
    }
}

/// <summary>
/// 列表块
/// </summary>
public sealed class ListBlock : DocumentBlock
{
    public required ListType ListType { get; init; }
    public required IReadOnlyList<string> Items { get; init; }

    public ListBlock()
    {
        Type = DocumentBlockType.List;
    }

    /// <summary>
    /// 列表的文本表示（用于向量化）
    /// </summary>
    public string Text => string.Join("\n", Items.Select((item, index) =>
        ListType == ListType.Ordered ? $"{index + 1}. {item}" : $"- {item}"));
}

/// <summary>
/// 表格块
/// </summary>
public sealed class TableBlock : DocumentBlock
{
    public required IReadOnlyList<IReadOnlyList<string>> Rows { get; init; }
    public required string Markdown { get; init; }
    public required string Hash { get; init; }
    public string? TableCaption { get; init; }

    public TableBlock()
    {
        Type = DocumentBlockType.Table;
    }

    /// <summary>
    /// 表格的文本表示（用于向量化）
    /// </summary>
    public string Text => (TableCaption != null ? TableCaption + "\n" : string.Empty) + Markdown;
}

/// <summary>
/// 图片块
/// </summary>
public sealed class ImageBlock : DocumentBlock
{
    public required byte[] ImageBytes { get; init; }
    public string? Description { get; init; } // AI生成的图片描述
    public string? ImagePath { get; init; } // 图片的有效路径（已解析）

    public ImageBlock()
    {
        Type = DocumentBlockType.Image;
    }

    /// <summary>
    /// 图片的文本表示（用于向量化）
    /// </summary>
    public string Text => Description ?? Caption ?? "[图片]";
}

/// <summary>
/// 列表类型
/// </summary>
public enum ListType
{
    /// <summary>
    /// 无序列表（bullet points）
    /// </summary>
    Unordered,

    /// <summary>
    /// 有序列表（numbered）
    /// </summary>
    Ordered
}

/// <summary>
/// DocumentBlock 扩展方法
/// </summary>
public static class DocumentBlockExtensions
{
    /// <summary>
    /// 获取任意文档块的文本表示（用于向量化）
    /// </summary>
    public static string GetText(this DocumentBlock block)
    {
        return block switch
        {
            TextBlock text => text.Text,
            HeadingBlock heading => heading.Text,
            ListBlock list => list.Text,
            TableBlock table => table.Text,
            ImageBlock image => image.Text,
            _ => block.Caption ?? string.Empty
        };
    }

    /// <summary>
    /// 判断块是否包含有效文本内容
    /// </summary>
    public static bool HasTextContent(this DocumentBlock block)
    {
        var text = block.GetText();
        return !string.IsNullOrWhiteSpace(text);
    }
}
