using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml.Drawing.Wordprocessing;
using DocumentFormat.OpenXml;
using MarketAssistant.Vectors.Interfaces;
using System.Security.Cryptography;
using System.Text;

namespace MarketAssistant.Vectors.Services;

/// <summary>
/// DOCX 读取器：按文档顺序提取文本段落、表格和图片
/// </summary>
public class DocxBlockReader : IDocumentBlockReader
{
    public bool CanRead(string filePath) => filePath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);

    public IEnumerable<DocumentBlock> ReadBlocks(Stream stream, string filePath)
    {
        stream.Position = 0;
        using var doc = WordprocessingDocument.Open(stream, false);
        var main = doc.MainDocumentPart;
        if (main?.Document?.Body == null) yield break;

        // 建立图片关系映射（一次性）
        var imageRelationMap = BuildImageRelationMap(main);
        var processedImages = new HashSet<string>();
        int order = 0;

        // 按文档顺序遍历所有元素
        foreach (var element in main.Document.Body.ChildElements)
        {
            switch (element)
            {
                case Paragraph paragraph:
                    // 处理段落中的图片（保持位置顺序）
                    foreach (var imageBlock in ExtractImagesFromParagraph(paragraph, imageRelationMap, processedImages, order++))
                    {
                        yield return imageBlock;
                    }

                    // 处理段落文本
                    var text = ExtractParagraphText(paragraph);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        yield return new DocumentBlock 
                        { 
                            Type = DocumentBlockType.Text, 
                            Text = text, 
                            Order = order++ 
                        };
                    }
                    break;

                case Table table:
                    var tableBlock = ProcessTable(table, order++);
                    if (tableBlock != null)
                    {
                        yield return tableBlock;
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// 建立图片关系映射（优化：只构建一次）
    /// </summary>
    private static Dictionary<string, ImagePart> BuildImageRelationMap(MainDocumentPart main)
    {
        var map = new Dictionary<string, ImagePart>(StringComparer.Ordinal);
        foreach (var imagePart in main.ImageParts)
        {
            var relationshipId = main.GetIdOfPart(imagePart);
            map[relationshipId] = imagePart;
        }
        return map;
    }

    /// <summary>
    /// 提取段落文本（处理复杂格式）
    /// </summary>
    private static string ExtractParagraphText(Paragraph paragraph)
    {
        // 使用 InnerText 获取纯文本，自动处理格式
        var text = paragraph.InnerText?.Trim();
        return string.IsNullOrWhiteSpace(text) ? string.Empty : text;
    }

    /// <summary>
    /// 从段落中提取图片（简化逻辑）
    /// </summary>
    private static IEnumerable<DocumentBlock> ExtractImagesFromParagraph(
        Paragraph paragraph, 
        Dictionary<string, ImagePart> imageRelationMap, 
        HashSet<string> processedImages,
        int order)
    {
        // 查找段落中的所有图片引用
        var blips = paragraph.Descendants<DocumentFormat.OpenXml.Drawing.Blip>();
        
        foreach (var blip in blips)
        {
            var embed = blip.Embed?.Value;
            if (string.IsNullOrEmpty(embed)) continue;
            
            // 避免重复处理同一图片
            if (!processedImages.Add(embed)) continue;
            
            if (imageRelationMap.TryGetValue(embed, out var imagePart))
            {
                var imageBytes = ExtractImageBytes(imagePart);
                if (imageBytes != null && imageBytes.Length > 0)
                {
                    yield return new DocumentBlock 
                    { 
                        Type = DocumentBlockType.Image, 
                        ImageBytes = imageBytes, 
                        Order = order
                    };
                }
            }
        }
    }

    /// <summary>
    /// 提取图片字节数据（优化异常处理）
    /// </summary>
    private static byte[]? ExtractImageBytes(ImagePart imagePart)
    {
        try
        {
            using var imageStream = imagePart.GetStream();
            if (imageStream.Length == 0) return null;
            
            using var ms = new MemoryStream((int)imageStream.Length);
            imageStream.CopyTo(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 处理表格（优化性能和准确性）
    /// </summary>
    private static DocumentBlock? ProcessTable(Table table, int order)
    {
        var rows = new List<List<string>>();
        
        foreach (var tableRow in table.Elements<TableRow>())
        {
            var row = ExtractTableRow(tableRow);
            if (row.Count > 0 && row.Any(cell => !string.IsNullOrWhiteSpace(cell)))
            {
                rows.Add(row);
            }
        }

        if (rows.Count == 0) return null;

        // 标准化列数（处理不规则表格）
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

        return new DocumentBlock
        {
            Type = DocumentBlockType.Table,
            Order = order,
            TableRows = rows.Select(r => (IReadOnlyList<string>)r.AsReadOnly()).ToList(),
            TableMarkdown = markdown,
            TableCaption = caption,
            TableHash = hash,
            Text = markdown,
            Caption = caption
        };
    }

    /// <summary>
    /// 提取表格行数据
    /// </summary>
    private static List<string> ExtractTableRow(TableRow tableRow)
    {
        var row = new List<string>();
        
        foreach (var tableCell in tableRow.Elements<TableCell>())
        {
            var cellText = ExtractTableCellText(tableCell);
            row.Add(cellText);
        }
        
        return row;
    }

    /// <summary>
    /// 提取表格单元格文本（处理多段落和格式）
    /// </summary>
    private static string ExtractTableCellText(TableCell cell)
    {
        var paragraphs = cell.Elements<Paragraph>()
            .Select(p => p.InnerText?.Trim())
            .Where(text => !string.IsNullOrWhiteSpace(text));
        
        return string.Join('\n', paragraphs);
    }

    /// <summary>
    /// 智能判断表格标题（改进逻辑）
    /// </summary>
    private static string? DetermineTableCaption(IReadOnlyList<List<string>> rows)
    {
        if (rows.Count == 0) return null;
        
        var firstRow = rows[0];
        
        // 判断是否为标题行的启发式规则
        var isLikelyHeader = firstRow.Count <= 8 && // 列数不太多
                           firstRow.All(cell => cell.Length <= 30) && // 单元格内容不太长
                           firstRow.Count(cell => !string.IsNullOrWhiteSpace(cell)) >= 2; // 至少有2个非空单元格
        
        return isLikelyHeader ? string.Join(" | ", firstRow.Where(c => !string.IsNullOrWhiteSpace(c))) : null;
    }

    /// <summary>
    /// 生成标准 Markdown 表格
    /// </summary>
    private static string GenerateTableMarkdown(IReadOnlyList<List<string>> rows)
    {
        if (rows.Count == 0) return string.Empty;
        
        var sb = new StringBuilder();
        sb.AppendLine("[TABLE]");
        
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

    /// <summary>
    /// Markdown 转义
    /// </summary>
    private static string EscapeMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        
        return text.Replace("|", "\\|")
                  .Replace("\n", "<br>")
                  .Replace("\r", "");
    }

    /// <summary>
    /// 计算哈希值（统一方法）
    /// </summary>
    private static string ComputeHash(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        
        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hashBytes);
    }
}
