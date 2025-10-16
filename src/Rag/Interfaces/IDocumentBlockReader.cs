namespace MarketAssistant.Rag.Interfaces;

/// <summary>
/// ֧��ͬʱ��ȡ�ı���ͼƬ���������ĵ���ȡ����
/// </summary>
public interface IDocumentBlockReader
{
    bool CanRead(string filePath);
    Task<IEnumerable<DocumentBlock>> ReadBlocksAsync(string filePath);
}

public enum DocumentBlockType
{
    /// <summary>
    /// ��ͨ�ı�����
    /// </summary>
    Text,

    /// <summary>
    /// ���⣨��ͬ����ı����в�ͬ��������Ҫ�ԣ�
    /// </summary>
    Heading,

    /// <summary>
    /// �б������/�����б���
    /// </summary>
    List,

    /// <summary>
    /// ��������
    /// </summary>
    Table,

    /// <summary>
    /// ͼƬ/ͼ��
    /// </summary>
    Image
}

/// <summary>
/// �ĵ������
/// </summary>
public abstract class DocumentBlock
{
    public DocumentBlockType Type { get; init; }
    public required int Order { get; init; }
    public string? Caption { get; set; }
}

/// <summary>
/// �ı���
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
/// �����
/// </summary>
public sealed class HeadingBlock : DocumentBlock
{
    public required string Text { get; init; }
    public required int Level { get; init; } // 1-6����ӦH1-H6

    public HeadingBlock()
    {
        Type = DocumentBlockType.Heading;
    }
}

/// <summary>
/// �б���
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
    /// �б����ı���ʾ��������������
    /// </summary>
    public string Text => string.Join("\n", Items.Select((item, index) =>
        ListType == ListType.Ordered ? $"{index + 1}. {item}" : $"- {item}"));
}

/// <summary>
/// �����
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
    /// ������ı���ʾ��������������
    /// </summary>
    public string Text => (TableCaption != null ? TableCaption + "\n" : string.Empty) + Markdown;
}

/// <summary>
/// ͼƬ��
/// </summary>
public sealed class ImageBlock : DocumentBlock
{
    public required byte[] ImageBytes { get; init; }
    public string? Description { get; init; } // AI���ɵ�ͼƬ����
    public string? ImagePath { get; init; } // ͼƬ����Ч·�����ѽ�����

    public ImageBlock()
    {
        Type = DocumentBlockType.Image;
    }

    /// <summary>
    /// ͼƬ���ı���ʾ��������������
    /// </summary>
    public string Text => Description ?? Caption ?? "[ͼƬ]";
}

/// <summary>
/// �б�����
/// </summary>
public enum ListType
{
    /// <summary>
    /// �����б���bullet points��
    /// </summary>
    Unordered,

    /// <summary>
    /// �����б���numbered��
    /// </summary>
    Ordered
}

/// <summary>
/// DocumentBlock ��չ����
/// </summary>
public static class DocumentBlockExtensions
{
    /// <summary>
    /// ��ȡ�����ĵ�����ı���ʾ��������������
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
    /// �жϿ��Ƿ������Ч�ı�����
    /// </summary>
    public static bool HasTextContent(this DocumentBlock block)
    {
        var text = block.GetText();
        return !string.IsNullOrWhiteSpace(text);
    }
}
