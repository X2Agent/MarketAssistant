using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MarketAssistant.Rag.Interfaces;
using System.Text;
using IOPath = System.IO.Path;

namespace MarketAssistant.Rag.Services;

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
    /// <summary>
    /// 单次转换的上下文状态：编号定义、图片引用、列表计数器等仅在本次转换内有效，
    /// 通过方法参数传递，保证转换器本身无共享可变状态（可安全注册为 Singleton 并发使用）。
    /// </summary>
    private sealed class ConversionContext
    {
        public readonly Dictionary<int, ListInfo> NumberingFormats = new();
        public readonly Dictionary<string, string> ImageReferences = new(StringComparer.Ordinal);
        public readonly Dictionary<int, int> ListItemCounters = new();
        public int ImageCounter;
    }

    private readonly IImageStorageService _imageStorageService;

    public DocxMarkdownConverter(IImageStorageService imageStorageService)
    {
        _imageStorageService = imageStorageService ?? throw new ArgumentNullException(nameof(imageStorageService));
    }

    public bool CanConvert(string filePath) =>
        filePath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);

    public async Task<string> ConvertToMarkdownAsync(string filePath)
    {
        return await ConvertCoreAsync(filePath);
    }

    private async Task<string> ConvertCoreAsync(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            using var doc = WordprocessingDocument.Open(stream, false);
            var main = doc.MainDocumentPart;
            if (main?.Document?.Body == null)
                return string.Empty;

            // 每次转换独立的上下文状态，转换器实例本身无共享可变状态
            var state = new ConversionContext();

            await ProcessNumberingDefinitionsAsync(doc, state);
            await ProcessImageReferencesAsync(doc, filePath, state);

            var markdown = new StringBuilder();
            var previousWasList = false;

            foreach (var element in main.Document.Body.ChildElements)
            {
                var isCurrentList = false;

                switch (element)
                {
                    case Paragraph paragraph:
                        isCurrentList = ProcessParagraph(paragraph, markdown, state);
                        break;

                    case Table table:
                        ProcessTable(table, markdown, state);
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

    private bool ProcessParagraph(Paragraph paragraph, StringBuilder markdown, ConversionContext state)
    {
        var numberingId = GetNumberingId(paragraph);
        var numberingLevel = GetNumberingLevel(paragraph);

        if (numberingId.HasValue)
        {
            ProcessListItem(paragraph, markdown, numberingId.Value, numberingLevel, state);
            return true;
        }

        var formattedText = ProcessTextFormatting(paragraph, state);

        if (string.IsNullOrWhiteSpace(formattedText))
        {
            markdown.AppendLine(); // 保留空行
            return false;
        }

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

    private void ProcessTable(Table table, StringBuilder markdown, ConversionContext state)
    {
        var rows = new List<List<string>>();

        foreach (var tableRow in table.Elements<TableRow>())
        {
            var row = new List<string>();
            foreach (var tableCell in tableRow.Elements<TableCell>())
            {
                var cellText = ExtractTableCellText(tableCell, state);
                row.Add(cellText);
            }

            if (row.Any(cell => !string.IsNullOrWhiteSpace(cell)))
            {
                rows.Add(row);
            }
        }

        if (rows.Count == 0) return;

        var maxColumns = rows.Max(r => r.Count);
        foreach (var row in rows)
        {
            while (row.Count < maxColumns)
            {
                row.Add(string.Empty);
            }
        }

        GenerateMarkdownTable(rows, markdown);
        markdown.AppendLine(); // 表格后空行
    }

    private int GetHeadingLevel(Paragraph paragraph)
    {
        var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (string.IsNullOrEmpty(style)) return 0;

        var styleLower = style.ToLowerInvariant();

        if (styleLower.StartsWith("heading"))
        {
            var levelStr = styleLower.Replace("heading", "");
            if (int.TryParse(levelStr, out var level) && level >= 1 && level <= 6)
                return level;
        }

        if (int.TryParse(styleLower, out var numLevel) && numLevel >= 1 && numLevel <= 6)
            return numLevel;

        var outlineLevel = paragraph.ParagraphProperties?.OutlineLevel?.Val?.Value;
        if (outlineLevel.HasValue && outlineLevel.Value >= 0 && outlineLevel.Value <= 5)
            return outlineLevel.Value + 1;

        return 0;
    }

    private string ProcessTextFormatting(Paragraph paragraph, ConversionContext state)
    {
        var result = new StringBuilder();

        foreach (var run in paragraph.Elements<Run>())
        {
            var drawing = run.Elements<Drawing>().FirstOrDefault();
            if (drawing != null)
            {
                var imageMarkdown = ProcessImage(drawing, state);
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

    private string ExtractTableCellText(TableCell cell, ConversionContext state)
    {
        var cellContent = new StringBuilder();

        foreach (var paragraph in cell.Elements<Paragraph>())
        {
            var formattedText = ProcessTextFormatting(paragraph, state);
            if (!string.IsNullOrWhiteSpace(formattedText))
            {
                if (cellContent.Length > 0)
                    cellContent.Append(" ");
                cellContent.Append(formattedText);
            }
        }

        return cellContent.ToString().Trim();
    }

    private void GenerateMarkdownTable(List<List<string>> rows, StringBuilder markdown)
    {
        if (rows.Count == 0) return;

        var header = rows[0];
        markdown.Append("| ");
        markdown.AppendJoin(" | ", header.Select(EscapeMarkdownTableCell));
        markdown.AppendLine(" |");

        markdown.Append("| ");
        markdown.AppendJoin(" | ", header.Select(_ => "---"));
        markdown.AppendLine(" |");

        foreach (var row in rows.Skip(1))
        {
            markdown.Append("| ");
            markdown.AppendJoin(" | ", row.Select(EscapeMarkdownTableCell));
            markdown.AppendLine(" |");
        }
    }

    private string EscapeMarkdownTableCell(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        return text.Replace("|", "\\|")
                  .Replace("\n", "<br>")
                  .Replace("\r", "")
                  .Trim();
    }

    private async Task ProcessNumberingDefinitionsAsync(WordprocessingDocument doc, ConversionContext state)
    {
        state.NumberingFormats.Clear();

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

                        var listInfo = new ListInfo
                        {
                            Level = levelValue,
                            IsOrdered = numFmt?.Value != NumberFormatValues.Bullet,
                            Prefix = numFmt?.Value == NumberFormatValues.Bullet ? "- " : "1. "
                        };

                        state.NumberingFormats[numId.Value] = listInfo;
                    }
                }
            }
        });
    }

    private int? GetNumberingId(Paragraph paragraph)
    {
        var numPr = paragraph.ParagraphProperties?.NumberingProperties;
        return numPr?.NumberingId?.Val?.Value;
    }

    private int GetNumberingLevel(Paragraph paragraph)
    {
        var numPr = paragraph.ParagraphProperties?.NumberingProperties;
        return numPr?.NumberingLevelReference?.Val?.Value ?? 0;
    }

    private void ProcessListItem(Paragraph paragraph, StringBuilder markdown, int numberingId, int level, ConversionContext state)
    {
        var text = ProcessTextFormatting(paragraph, state);
        if (string.IsNullOrWhiteSpace(text)) return;

        var listInfo = state.NumberingFormats.GetValueOrDefault(numberingId, new ListInfo { Prefix = "- ", IsOrdered = false });

        var indent = new string(' ', level * 2);

        if (listInfo.IsOrdered)
        {
            var counterKey = numberingId * 100 + level; // 组合键考虑级别
            if (!state.ListItemCounters.ContainsKey(counterKey))
                state.ListItemCounters[counterKey] = 1;

            markdown.AppendLine($"{indent}{state.ListItemCounters[counterKey]}. {text}");
            state.ListItemCounters[counterKey]++;
        }
        else
        {
            markdown.AppendLine($"{indent}- {text}");
        }
    }

    private async Task ProcessImageReferencesAsync(WordprocessingDocument doc, string documentPath, ConversionContext state)
    {
        state.ImageReferences.Clear();

        await Task.Run(async () =>
        {
            var imageParts = doc.MainDocumentPart?.ImageParts;
            if (imageParts == null || doc.MainDocumentPart == null) return;

            foreach (var imagePart in imageParts)
            {
                try
                {
                    var relationshipId = doc.MainDocumentPart.GetIdOfPart(imagePart);
                    state.ImageCounter++;

                    var imageFileName = $"doc_image{state.ImageCounter}.{GetImageExtension(imagePart.ContentType)}";

                    using var stream = imagePart.GetStream();
                    using var memoryStream = new MemoryStream();
                    await stream.CopyToAsync(memoryStream);
                    var imageBytes = memoryStream.ToArray();

                    var imagePath = await _imageStorageService.SaveImageAsync(imageBytes, imageFileName, documentPath);

                    state.ImageReferences[relationshipId] = imagePath;
                }
                catch (Exception ex)
                {
                    // 记录错误，但继续处理
                    System.Diagnostics.Debug.WriteLine($"Failed to process image: {ex.Message}");
                }
            }
        });
    }

    private string ProcessImage(Drawing drawing, ConversionContext state)
    {
        try
        {
            var blip = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().FirstOrDefault();
            if (blip?.Embed?.Value == null) return string.Empty;

            var relationshipId = blip.Embed.Value;
            if (state.ImageReferences.TryGetValue(relationshipId, out var imagePath))
            {
                // 从文件路径中提取图片序号来生成有意义的alt文本
                var fileName = IOPath.GetFileNameWithoutExtension(imagePath);
                var imageNumber = fileName.Replace("doc_image", "");
                var altText = $"文档图片{imageNumber}";

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
                cleanedLines.Add(line.TrimEnd());
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