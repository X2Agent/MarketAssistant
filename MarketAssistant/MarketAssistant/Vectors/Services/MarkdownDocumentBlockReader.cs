using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarketAssistant.Vectors.Interfaces;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MarketAssistant.Vectors.Services;

/// <summary>
/// 统一的Markdown文档块读取器
/// 负责将Markdown文本解析为结构化的DocumentBlock集合
/// 支持文本、标题、列表、表格、代码块、引用等类型
/// 集成统一的图片路径处理逻辑
/// </summary>
public class MarkdownDocumentBlockReader : IDocumentBlockReader
{
    private readonly MarkdownConverterFactory _converterFactory;
    private readonly IImageStorageService _imageStorageService;
    private static readonly Regex ImagePattern = new(@"!\[([^\]]*)\]\(([^)]+)\)", RegexOptions.Compiled);

    public MarkdownDocumentBlockReader(MarkdownConverterFactory converterFactory, IImageStorageService imageStorageService)
    {
        _converterFactory = converterFactory ?? throw new ArgumentNullException(nameof(converterFactory));
        _imageStorageService = imageStorageService ?? throw new ArgumentNullException(nameof(imageStorageService));
    }

    public bool CanRead(string filePath)
    {
        // 支持直接的Markdown文件或可转换为Markdown的文件
        return filePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
               filePath.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase) ||
               _converterFactory.CanConvert(filePath);
    }

    public async Task<IEnumerable<DocumentBlock>> ReadBlocksAsync(string filePath)
    {
        string markdown;

        // 如果是Markdown文件，直接读取
        if (filePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
            filePath.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
        {
            markdown = await File.ReadAllTextAsync(filePath);
        }
        else
        {
            // 其他格式，先转换为Markdown
            var converter = _converterFactory.GetConverter(filePath);
            if (converter == null)
            {
                return Enumerable.Empty<DocumentBlock>();
            }

            markdown = await converter.ConvertToMarkdownAsync(filePath);
        }

        if (string.IsNullOrWhiteSpace(markdown))
        {
            return Enumerable.Empty<DocumentBlock>();
        }

        // 解析Markdown为DocumentBlock
        return await ParseMarkdownToBlocksAsync(markdown, filePath);
    }

    private async Task<IEnumerable<DocumentBlock>> ParseMarkdownToBlocksAsync(string markdown, string filePath)
    {
        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

        var document = Markdig.Markdown.Parse(markdown, pipeline);
        var blocks = new List<DocumentBlock>();
        int order = 0;

        foreach (var block in document)
        {
            var documentBlocks = await ConvertMarkdownBlockToDocumentBlocksAsync(block, order, filePath);
            blocks.AddRange(documentBlocks);
            order += documentBlocks.Count;
        }

        return blocks;
    }

    private async Task<List<DocumentBlock>> ConvertMarkdownBlockToDocumentBlocksAsync(Block block, int startOrder, string filePath)
    {
        var results = new List<DocumentBlock>();
        int order = startOrder;

        switch (block)
        {
            case Markdig.Syntax.HeadingBlock heading:
                var headingText = ExtractTextFromBlock(heading);
                if (!string.IsNullOrWhiteSpace(headingText))
                {
                    results.Add(new Interfaces.HeadingBlock
                    {
                        Order = order++,
                        Text = headingText,
                        Level = heading.Level
                    });
                }
                break;

            case ParagraphBlock paragraph:
                // 首先提取图片块
                var imageBlocks = await ExtractImageBlocksFromParagraphAsync(paragraph, order, filePath);
                results.AddRange(imageBlocks);
                order += imageBlocks.Count;

                // 然后处理文本内容（排除图片）
                var paragraphText = ExtractTextFromBlock(paragraph);
                if (!string.IsNullOrWhiteSpace(paragraphText))
                {
                    results.Add(new TextBlock
                    {
                        Order = order++,
                        Text = paragraphText
                    });
                }
                break;

            case Markdig.Syntax.ListBlock list:
                var listItems = ExtractListItems(list);
                if (listItems.Count > 0)
                {
                    var listType = list.IsOrdered ? ListType.Ordered : ListType.Unordered;
                    results.Add(new Interfaces.ListBlock
                    {
                        Order = order++,
                        ListType = listType,
                        Items = listItems
                    });
                }
                break;

            case Table table:
                var tableBlock = ProcessMarkdownTable(table, order++);
                if (tableBlock != null)
                {
                    results.Add(tableBlock);
                }
                break;

            default:
                // 处理其他类型的块作为普通文本
                var defaultText = ExtractTextFromBlock(block);
                if (!string.IsNullOrWhiteSpace(defaultText))
                {
                    results.Add(new TextBlock
                    {
                        Order = order++,
                        Text = defaultText
                    });
                }
                break;
        }

        return results;
    }

    private string ExtractTextFromBlock(Block block)
    {
        var sb = new StringBuilder();
        ExtractTextRecursive(block, sb);
        return sb.ToString().Trim();
    }

    private void ExtractTextRecursive(MarkdownObject obj, StringBuilder sb)
    {
        switch (obj)
        {
            case LiteralInline literal:
                sb.Append(literal.Content);
                break;
            case LineBreakInline:
                sb.AppendLine();
                break;
            case CodeInline code:
                sb.Append($"`{code.Content}`");
                break;
            case LinkInline link when link.IsImage:
                // 跳过图片内联元素，不添加到文本中（图片将单独处理）
                break;
            case LinkInline link:
                // 处理普通链接
                foreach (var child in link)
                {
                    ExtractTextRecursive(child, sb);
                }
                break;
            case EmphasisInline emphasis:
                var marker = emphasis.DelimiterChar == '*' ? "*" : "_";
                var count = emphasis.DelimiterCount;
                sb.Append(new string(marker[0], count));

                foreach (var child in emphasis)
                {
                    ExtractTextRecursive(child, sb);
                }

                sb.Append(new string(marker[0], count));
                break;
            case ContainerBlock container:
                foreach (var child in container)
                {
                    ExtractTextRecursive(child, sb);
                    if (child is Block)
                    {
                        sb.AppendLine();
                    }
                }
                break;
            case ContainerInline containerInline:
                foreach (var child in containerInline)
                {
                    ExtractTextRecursive(child, sb);
                }
                break;
            case LeafBlock leaf:
                if (leaf.Inline != null)
                {
                    ExtractTextRecursive(leaf.Inline, sb);
                }
                break;
        }
    }

    private async Task<List<DocumentBlock>> ExtractImageBlocksFromParagraphAsync(ParagraphBlock paragraph, int startOrder, string filePath)
    {
        var imageBlocks = new List<DocumentBlock>();
        int order = startOrder;

        // 递归遍历段落中的所有内联元素，查找图片
        var extractedImages = await ExtractImagesRecursiveAsync(paragraph.Inline, filePath);

        foreach (var (altText, imagePath, imageBytes) in extractedImages)
        {
            // 只保存解析后的有效路径
            var resolvedPath = string.IsNullOrEmpty(imagePath) ? null : _imageStorageService.ResolveImagePath(imagePath, filePath);

            imageBlocks.Add(new ImageBlock
            {
                Order = order++,
                ImageBytes = imageBytes,
                Description = altText,
                Caption = altText,
                ImagePath = resolvedPath
            });
        }

        return imageBlocks;
    }

    private async Task<List<(string AltText, string ImagePath, byte[] ImageBytes)>> ExtractImagesRecursiveAsync(ContainerInline? container, string filePath)
    {
        var images = new List<(string, string, byte[])>();

        if (container == null) return images;

        foreach (var inline in container)
        {
            switch (inline)
            {
                case LinkInline link when link.IsImage:
                    var altText = ExtractTextFromInline(link);
                    var imagePath = link.Url ?? string.Empty;

                    // 使用统一的图片路径解析逻辑
                    byte[] imageBytes = Array.Empty<byte>();
                    try
                    {
                        string resolvedImagePath = _imageStorageService.ResolveImagePath(imagePath, filePath);

                        if (File.Exists(resolvedImagePath))
                        {
                            imageBytes = await File.ReadAllBytesAsync(resolvedImagePath);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Image file not found: {resolvedImagePath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        // 记录警告但继续处理，使用空字节数组
                        System.Diagnostics.Debug.WriteLine($"Failed to load image {imagePath}: {ex.Message}");
                    }

                    images.Add((altText, imagePath, imageBytes));
                    break;

                case ContainerInline containerInline:
                    // 递归处理嵌套的内联容器
                    var nestedImages = await ExtractImagesRecursiveAsync(containerInline, filePath);
                    images.AddRange(nestedImages);
                    break;
            }
        }

        return images;
    }

    private string ExtractTextFromInline(ContainerInline container)
    {
        var sb = new StringBuilder();
        foreach (var child in container)
        {
            if (child is LiteralInline literal)
            {
                sb.Append(literal.Content);
            }
        }
        return sb.ToString().Trim();
    }

    private List<string> ExtractListItems(Markdig.Syntax.ListBlock list)
    {
        var items = new List<string>();

        foreach (var item in list)
        {
            if (item is ListItemBlock listItem)
            {
                var itemText = ExtractTextFromBlock(listItem);
                if (!string.IsNullOrWhiteSpace(itemText))
                {
                    items.Add(itemText);
                }
            }
        }

        return items;
    }

    private TableBlock? ProcessMarkdownTable(Table table, int order)
    {
        var rows = new List<List<string>>();

        foreach (var row in table)
        {
            if (row is TableRow tableRow)
            {
                var rowData = new List<string>();
                foreach (var cell in tableRow)
                {
                    if (cell is TableCell tableCell)
                    {
                        var cellText = ExtractTextFromBlock(tableCell);
                        rowData.Add(cellText);
                    }
                }

                if (rowData.Any(cell => !string.IsNullOrWhiteSpace(cell)))
                {
                    rows.Add(rowData);
                }
            }
        }

        if (rows.Count == 0) return null;

        // 标准化列数
        var maxColumns = rows.Max(r => r.Count);
        foreach (var row in rows)
        {
            while (row.Count < maxColumns)
            {
                row.Add(string.Empty);
            }
        }

        var tableText = string.Join('|', rows.SelectMany(r => r));
        var hash = ComputeHash(tableText);
        var markdown = GenerateTableMarkdown(rows);
        var caption = DetermineTableCaption(rows);

        return new MarketAssistant.Vectors.Interfaces.TableBlock
        {
            Order = order,
            Rows = rows.Select(r => (IReadOnlyList<string>)r.AsReadOnly()).ToList(),
            Markdown = markdown,
            Hash = hash,
            TableCaption = caption
        };
    }

    private static string? DetermineTableCaption(IReadOnlyList<List<string>> rows)
    {
        if (rows.Count == 0) return null;

        var firstRow = rows[0];
        var isLikelyHeader = firstRow.Count <= 8 &&
                           firstRow.All(cell => cell.Length <= 50) &&
                           firstRow.Count(cell => !string.IsNullOrWhiteSpace(cell)) >= 2;

        return isLikelyHeader ? string.Join(" | ", firstRow.Where(c => !string.IsNullOrWhiteSpace(c))) : null;
    }

    private static string GenerateTableMarkdown(IReadOnlyList<List<string>> rows)
    {
        if (rows.Count == 0) return string.Empty;

        var sb = new StringBuilder();

        // 表头
        var header = rows[0];
        sb.Append("| ").AppendJoin(" | ", header.Select(EscapeMarkdown)).AppendLine(" |");

        // 分隔线
        sb.Append("| ").AppendJoin(" | ", header.Select(_ => "---")).AppendLine(" |");

        // 数据行
        foreach (var row in rows.Skip(1))
        {
            sb.Append("| ").AppendJoin(" | ", row.Select(EscapeMarkdown)).AppendLine(" |");
        }

        return sb.ToString();
    }

    private static string EscapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        return text.Replace("|", "\\|")
                  .Replace("\n", "<br>")
                  .Replace("\r", "");
    }

    private static string ComputeHash(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hashBytes);
    }
}
