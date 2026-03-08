using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MarketAssistant.Rag.Interfaces;
using System.Text;
using IOPath = System.IO.Path;

namespace MarketAssistant.Rag.Services;

/// <summary>
/// 列表信息
/// </summary>
public class ListInfo
{
    public string Prefix { get; set; } = string.Empty;
    public bool IsOrdered { get; set; }
    public int Level { get; set; }
}

/// <summary>
/// DOCX到Markdown转换器
/// 重新实现，准确处理文档中的各级标题、文本段落、表格、图片和列表元素
/// </summary>
public class DocxMarkdownConverter : IMarkdownConverter
{
    private readonly Dictionary<int, ListInfo> _numberingFormats = new();
    private readonly Dictionary<string, string> _imageReferences = new();
    private readonly IImageStorageService _imageStorageService;
    private readonly Dictionary<int, int> _listItemCounters = new();
    private int _imageCounter = 0;

    public DocxMarkdownConverter(IImageStorageService imageStorageService)
    {
        _imageStorageService = imageStorageService ?? throw new ArgumentNullException(nameof(imageStorageService));
    }

    public bool CanConvert(string filePath) =>
        filePath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);

    public async Task<string> ConvertToMarkdownAsync(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            using var doc = WordprocessingDocument.Open(stream, false);
            var main = doc.MainDocumentPart;
            if (main?.Document?.Body == null)
                return string.Empty;

            // 清理之前的状态
            _numberingFormats.Clear();
            _imageReferences.Clear();
            _listItemCounters.Clear();
            _imageCounter = 0;

            // 预处理编号定义和图片
            await ProcessNumberingDefinitionsAsync(doc);
            await ProcessImageReferencesAsync(doc, filePath);

            var markdown = new StringBuilder();
            var previousWasList = false;

            // 按文档顺序处理所有元素
            foreach (var element in main.Document.Body.ChildElements)
            {
                var isCurrentList = false;

                switch (element)
                {
                    case Paragraph paragraph:
                        isCurrentList = ProcessParagraph(paragraph, markdown);
                        break;

                    case Table table:
                        ProcessTable(table, markdown);
                        break;

                    default:
                        // 对于不认识的元素，尝试提取其文本内容
                        var textContent = element.InnerText?.Trim();
                        if (!string.IsNullOrWhiteSpace(textContent))
                        {
                            markdown.AppendLine(textContent);
                            markdown.AppendLine();
                        }
                        break;
                }

                // 如果从列表切换到非列表，添加额外的空行
                if (previousWasList && !isCurrentList)
                {
                    markdown.AppendLine();
                }

                previousWasList = isCurrentList;
            }

            return CleanupMarkdown(markdown.ToString());
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"转换DOCX文件时出错: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 处理段落元素
    /// </summary>
    /// <param name="paragraph">段落</param>
    /// <param name="markdown">markdown构建器</param>
    /// <returns>是否为列表项</returns>
    private bool ProcessParagraph(Paragraph paragraph, StringBuilder markdown)
    {
        // 检查是否为列表项
        var numberingId = GetNumberingId(paragraph);
        var numberingLevel = GetNumberingLevel(paragraph);

        if (numberingId.HasValue)
        {
            ProcessListItem(paragraph, markdown, numberingId.Value, numberingLevel);
            return true;
        }

        // 处理段落格式化文本（包括图片）
        var formattedText = ProcessTextFormatting(paragraph);

        if (string.IsNullOrWhiteSpace(formattedText))
        {
            markdown.AppendLine(); // 保留空行
            return false;
        }

        // 检查是否为标题
        var headingLevel = GetHeadingLevel(paragraph);
        if (headingLevel > 0)
        {
            markdown.AppendLine($"{new string('#', headingLevel)} {formattedText}");
        }
        else
        {
            markdown.AppendLine(formattedText);
        }

        markdown.AppendLine(); // 段落间空行
        return false;
    }

    /// <summary>
    /// 处理表格元素
    /// </summary>
    private void ProcessTable(Table table, StringBuilder markdown)
    {
        var rows = new List<List<string>>();

        foreach (var tableRow in table.Elements<TableRow>())
        {
            var row = new List<string>();
            foreach (var tableCell in tableRow.Elements<TableCell>())
            {
                var cellText = ExtractTableCellText(tableCell);
                row.Add(cellText);
            }

            if (row.Any(cell => !string.IsNullOrWhiteSpace(cell)))
            {
                rows.Add(row);
            }
        }

        if (rows.Count == 0) return;

        // 标准化列数
        var maxColumns = rows.Max(r => r.Count);
        foreach (var row in rows)
        {
            while (row.Count < maxColumns)
            {
                row.Add(string.Empty);
            }
        }

        // 生成Markdown表格
        GenerateMarkdownTable(rows, markdown);
        markdown.AppendLine(); // 表格后空行
    }

    /// <summary>
    /// 获取段落的标题级别
    /// </summary>
    private int GetHeadingLevel(Paragraph paragraph)
    {
        var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (string.IsNullOrEmpty(style)) return 0;

        var styleLower = style.ToLowerInvariant();

        // 检查标准的Word标题样式
        if (styleLower.StartsWith("heading"))
        {
            var levelStr = styleLower.Replace("heading", "");
            if (int.TryParse(levelStr, out var level) && level >= 1 && level <= 6)
                return level;
        }

        // 检查数字样式
        if (int.TryParse(styleLower, out var numLevel) && numLevel >= 1 && numLevel <= 6)
            return numLevel;

        // 检查大纲级别
        var outlineLevel = paragraph.ParagraphProperties?.OutlineLevel?.Val?.Value;
        if (outlineLevel.HasValue && outlineLevel.Value >= 0 && outlineLevel.Value <= 5)
            return outlineLevel.Value + 1;

        return 0;
    }

    /// <summary>
    /// 处理文本格式化
    /// </summary>
    private string ProcessTextFormatting(Paragraph paragraph)
    {
        var result = new StringBuilder();

        foreach (var run in paragraph.Elements<Run>())
        {
            // 检查是否包含图片
            var drawing = run.Elements<Drawing>().FirstOrDefault();
            if (drawing != null)
            {
                var imageMarkdown = ProcessImage(drawing);
                if (!string.IsNullOrEmpty(imageMarkdown))
                {
                    result.Append(imageMarkdown);
                    continue;
                }
            }

            var text = run.InnerText;
            if (string.IsNullOrEmpty(text)) continue;

            var runProps = run.RunProperties;
            if (runProps != null)
            {
                var isBold = runProps.Bold != null && (runProps.Bold.Val == null || runProps.Bold.Val.Value);
                var isItalic = runProps.Italic != null && (runProps.Italic.Val == null || runProps.Italic.Val.Value);
                var isStrikethrough = runProps.Strike != null && (runProps.Strike.Val == null || runProps.Strike.Val.Value);

                // 处理下划线（在Markdown中用斜体表示）
                var isUnderline = runProps.Underline != null && runProps.Underline.Val?.Value != UnderlineValues.None;

                if (isBold && isItalic)
                    text = $"***{text}***";
                else if (isBold)
                    text = $"**{text}**";
                else if (isItalic || isUnderline)
                    text = $"*{text}*";

                if (isStrikethrough)
                    text = $"~~{text}~~";
            }

            result.Append(text);
        }

        return result.ToString();
    }

    /// <summary>
    /// 提取表格单元格文本
    /// </summary>
    private string ExtractTableCellText(TableCell cell)
    {
        var cellContent = new StringBuilder();

        foreach (var paragraph in cell.Elements<Paragraph>())
        {
            var formattedText = ProcessTextFormatting(paragraph);
            if (!string.IsNullOrWhiteSpace(formattedText))
            {
                if (cellContent.Length > 0)
                    cellContent.Append(" ");
                cellContent.Append(formattedText);
            }
        }

        return cellContent.ToString().Trim();
    }

    /// <summary>
    /// 生成Markdown表格
    /// </summary>
    private void GenerateMarkdownTable(List<List<string>> rows, StringBuilder markdown)
    {
        if (rows.Count == 0) return;

        // 表头
        var header = rows[0];
        markdown.Append("| ");
        markdown.AppendJoin(" | ", header.Select(EscapeMarkdownTableCell));
        markdown.AppendLine(" |");

        // 分隔线
        markdown.Append("| ");
        markdown.AppendJoin(" | ", header.Select(_ => "---"));
        markdown.AppendLine(" |");

        // 数据行
        foreach (var row in rows.Skip(1))
        {
            markdown.Append("| ");
            markdown.AppendJoin(" | ", row.Select(EscapeMarkdownTableCell));
            markdown.AppendLine(" |");
        }
    }

    /// <summary>
    /// 转义Markdown表格单元格内容
    /// </summary>
    private string EscapeMarkdownTableCell(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        return text.Replace("|", "\\|")
                  .Replace("\n", "<br>")
                  .Replace("\r", "")
                  .Trim();
    }

    /// <summary>
    /// 预处理编号定义
    /// </summary>
    private async Task ProcessNumberingDefinitionsAsync(WordprocessingDocument doc)
    {
        _numberingFormats.Clear();

        await Task.Run(() =>
        {
            var numberingPart = doc.MainDocumentPart?.NumberingDefinitionsPart;
            if (numberingPart?.Numbering == null) return;

            foreach (var num in numberingPart.Numbering.Elements<NumberingInstance>())
            {
                var numId = num.NumberID?.Value;
                var abstractNumId = num.AbstractNumId?.Val?.Value;

                if (numId.HasValue && abstractNumId.HasValue)
                {
                    var abstractNum = numberingPart.Numbering.Elements<AbstractNum>()
                        .FirstOrDefault(an => an.AbstractNumberId?.Value == abstractNumId);

                    if (abstractNum != null)
                    {
                        var level = abstractNum.Elements<Level>().FirstOrDefault();
                        var numFmt = level?.NumberingFormat?.Val;
                        var levelValue = level?.LevelIndex?.Value ?? 0;

                        // 根据编号格式确定列表类型
                        var listInfo = new ListInfo
                        {
                            Level = levelValue,
                            IsOrdered = numFmt?.Value != NumberFormatValues.Bullet,
                            Prefix = numFmt?.Value == NumberFormatValues.Bullet ? "- " : "1. "
                        };

                        _numberingFormats[numId.Value] = listInfo;
                    }
                }
            }
        });
    }

    /// <summary>
    /// 获取段落的编号ID
    /// </summary>
    private int? GetNumberingId(Paragraph paragraph)
    {
        var numPr = paragraph.ParagraphProperties?.NumberingProperties;
        return numPr?.NumberingId?.Val?.Value;
    }

    /// <summary>
    /// 获取段落的编号级别
    /// </summary>
    private int GetNumberingLevel(Paragraph paragraph)
    {
        var numPr = paragraph.ParagraphProperties?.NumberingProperties;
        return numPr?.NumberingLevelReference?.Val?.Value ?? 0;
    }

    /// <summary>
    /// 处理列表项
    /// </summary>
    private void ProcessListItem(Paragraph paragraph, StringBuilder markdown, int numberingId, int level)
    {
        var text = ProcessTextFormatting(paragraph);
        if (string.IsNullOrWhiteSpace(text)) return;

        var listInfo = _numberingFormats.GetValueOrDefault(numberingId, new ListInfo { Prefix = "- ", IsOrdered = false });

        // 添加缩进
        var indent = new string(' ', level * 2);

        if (listInfo.IsOrdered)
        {
            // 为有序列表维护计数器
            var counterKey = numberingId * 100 + level; // 组合键考虑级别
            if (!_listItemCounters.ContainsKey(counterKey))
                _listItemCounters[counterKey] = 1;

            markdown.AppendLine($"{indent}{_listItemCounters[counterKey]}. {text}");
            _listItemCounters[counterKey]++;
        }
        else
        {
            markdown.AppendLine($"{indent}- {text}");
        }
    }

    /// <summary>
    /// 预处理图片引用
    /// </summary>
    private async Task ProcessImageReferencesAsync(WordprocessingDocument doc, string documentPath)
    {
        _imageReferences.Clear();

        await Task.Run(async () =>
        {
            var imageParts = doc.MainDocumentPart?.ImageParts;
            if (imageParts == null || doc.MainDocumentPart == null) return;

            foreach (var imagePart in imageParts)
            {
                try
                {
                    var relationshipId = doc.MainDocumentPart.GetIdOfPart(imagePart);
                    _imageCounter++;

                    // 使用 IImageStorageService 保存图片
                    var imageFileName = $"doc_image{_imageCounter}.{GetImageExtension(imagePart.ContentType)}";

                    using var stream = imagePart.GetStream();
                    using var memoryStream = new MemoryStream();
                    await stream.CopyToAsync(memoryStream);
                    var imageBytes = memoryStream.ToArray();

                    // 通过 IImageStorageService 保存图片并获取路径
                    var imagePath = await _imageStorageService.SaveImageAsync(imageBytes, imageFileName, documentPath);

                    // 存储完整的绝对路径
                    _imageReferences[relationshipId] = imagePath;
                }
                catch (Exception ex)
                {
                    // 记录错误，但继续处理
                    System.Diagnostics.Debug.WriteLine($"Failed to process image: {ex.Message}");
                }
            }
        });
    }

    /// <summary>
    /// 处理图片元素
    /// </summary>
    private string ProcessImage(Drawing drawing)
    {
        try
        {
            var blip = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().FirstOrDefault();
            if (blip?.Embed?.Value == null) return string.Empty;

            var relationshipId = blip.Embed.Value;
            if (_imageReferences.TryGetValue(relationshipId, out var imagePath))
            {
                // 从文件路径中提取图片序号来生成有意义的alt文本
                var fileName = IOPath.GetFileNameWithoutExtension(imagePath);
                var imageNumber = fileName.Replace("doc_image", "");
                var altText = $"文档图片{imageNumber}";

                // 使用绝对路径，转换为URI格式
                var fileUri = new Uri(imagePath).AbsoluteUri;
                return $"![{altText}]({fileUri})";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to process image drawing: {ex.Message}");
        }

        return string.Empty;
    }

    /// <summary>
    /// 根据内容类型获取图片扩展名
    /// </summary>
    private static string GetImageExtension(string contentType)
    {
        return contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => "jpg",
            "image/jpg" => "jpg",
            "image/png" => "png",
            "image/gif" => "gif",
            "image/bmp" => "bmp",
            "image/tiff" => "tiff",
            "image/webp" => "webp",
            _ => "png"
        };
    }

    /// <summary>
    /// 清理和规范化Markdown内容
    /// </summary>
    private string CleanupMarkdown(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return string.Empty;

        // 移除多余的空行
        var lines = markdown.Split('\n', StringSplitOptions.None);
        var cleanedLines = new List<string>();
        var consecutiveEmptyLines = 0;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            if (string.IsNullOrEmpty(trimmedLine))
            {
                consecutiveEmptyLines++;
                if (consecutiveEmptyLines <= 2) // 最多保留两个连续空行
                {
                    cleanedLines.Add(string.Empty);
                }
            }
            else
            {
                consecutiveEmptyLines = 0;
                cleanedLines.Add(line.TrimEnd()); // 移除行尾空白
            }
        }

        // 移除开头和结尾的空行
        while (cleanedLines.Count > 0 && string.IsNullOrEmpty(cleanedLines[0]))
            cleanedLines.RemoveAt(0);

        while (cleanedLines.Count > 0 && string.IsNullOrEmpty(cleanedLines[cleanedLines.Count - 1]))
            cleanedLines.RemoveAt(cleanedLines.Count - 1);

        return string.Join("\n", cleanedLines);
    }
}