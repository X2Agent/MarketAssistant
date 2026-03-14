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

public enum DocumentBlockType
{
    /// <summary>
    /// 普通文本段落
    /// </summary>
    Text,

    /// <summary>
    /// 标题（Heading），有层级信息
    /// </summary>
    Heading,

    /// <summary>
    /// 列表（有序或无序）
    /// </summary>
    List,

    /// <summary>
    /// 表格数据
    /// </summary>
    Table,

    /// <summary>
    /// 图片或视觉内容块
    /// </summary>
    Image
}

/// <summary>
/// 文档块的基类，包含公共属性（类型、顺序、标题/说明等）。
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
/// 标题块，包含层级（Level 对应 H1-H6）
/// </summary>
public sealed class HeadingBlock : DocumentBlock
{
    public required string Text { get; init; }
    public required int Level { get; init; } // 1-6 对应 H1-H6

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
    /// 将列表项合并为文本表示（用于索引或展示）
    /// </summary>
    public string Text => string.Join("\n", Items.Select((item, index) =>
        ListType == ListType.Ordered ? $"{index + 1}. {item}" : $"- {item}"));
}

/// <summary>
/// 表格块，包含原始 Markdown 表示与行数据
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
    /// 表格的文本表示，包含可选标题和 Markdown 内容
    /// </summary>
    public string Text => (TableCaption != null ? TableCaption + "\n" : string.Empty) + Markdown;
}

/// <summary>
/// 图片块，包含字节数据和可选描述
/// </summary>
public sealed class ImageBlock : DocumentBlock
{
    public required byte[] ImageBytes { get; init; }
    public string? Description { get; init; } // AI 生成的图片描述
    public string? ImagePath { get; init; } // 图片的原始路径（如果有）

    public ImageBlock()
    {
        Type = DocumentBlockType.Image;
    }

    /// <summary>
    /// 图片的文本表示，优先使用 AI 描述，其次使用 Caption，最后使用占位符
    /// </summary>
    public string Text => Description ?? Caption ?? "[图片]";
}

/// <summary>
/// 列表类型枚举
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
/// DocumentBlock 的扩展方法
/// </summary>
public static class DocumentBlockExtensions
{
    /// <summary>
    /// 获取块的文本表示，便于索引或拼接
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
    /// 判断块是否包含可用于索引的文本内容
    /// </summary>
    public static bool HasTextContent(this DocumentBlock block)
    {
        var text = block.GetText();
        return !string.IsNullOrWhiteSpace(text);
    }
}
