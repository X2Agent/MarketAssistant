using MarketAssistant.Vectors.Interfaces;
using PdfPage = UglyToad.PdfPig.Content.Page;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;

namespace MarketAssistant.Vectors.Services;

/// <summary>
/// PDF 读取器：支持结构化块读取和原始文本读取的统一实现
/// </summary>
public class PdfReader : IDocumentBlockReader, IRawDocumentReader
{
    private static readonly Regex ParagraphSeparator = new(@"\n\s*\n", RegexOptions.Compiled);
    private static readonly Regex WhitespaceNormalizer = new(@"\s+", RegexOptions.Compiled);
    
    // 配置参数
    private const int MinParagraphLength = 15;
    private const int MinImageSize = 2048; // 2KB
    private const double LineHeightTolerance = 3.0;
    private const double ColumnAlignmentTolerance = 20.0;

    public bool CanRead(string filePath) => 
        filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 实现 IRawDocumentReader：提取纯文本内容（降级方案）
    /// </summary>
    public string ReadAllText(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        
        stream.Position = 0;
        using var pdf = PdfDocument.Open(stream);
        var sb = new StringBuilder();
        
        for (int i = 1; i <= pdf.NumberOfPages; i++)
        {
            var page = pdf.GetPage(i);
            var text = page.Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                // 将 PDF 页内的单行换行转为空格，页与页之间保留空行
                text = text.Replace("\r\n", "\n").Replace("\r", "\n");
                var lines = text.Split('\n');
                var line = string.Join(' ', lines.Select(s => s.Trim()).Where(s => s.Length > 0));
                if (line.Length > 0)
                {
                    sb.AppendLine(line);
                    sb.AppendLine();
                }
            }
        }
        return sb.ToString();
    }

    public IEnumerable<DocumentBlock> ReadBlocks(Stream stream, string filePath)
    {
        ArgumentNullException.ThrowIfNull(stream);
        
        stream.Position = 0;
        using var pdf = PdfDocument.Open(stream);
        int globalOrder = 0;
        
        for (int pageNumber = 1; pageNumber <= pdf.NumberOfPages; pageNumber++)
        {
            var pageBlocks = ProcessPage(pdf, pageNumber, globalOrder);
            foreach (var block in pageBlocks)
            {
                yield return block;
                globalOrder++;
            }
        }
    }

    /// <summary>
    /// 处理单个页面，提取所有块元素
    /// </summary>
    private List<DocumentBlock> ProcessPage(PdfDocument pdf, int pageNumber, int startOrder)
    {
        var result = new List<DocumentBlock>();
        PdfPage page;
        
        try
        {
            page = pdf.GetPage(pageNumber);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to read page {pageNumber}: {ex.Message}");
            return result;
        }

        var elements = new List<PageElement>();

        // 提取文本 - 优先级最高，因为最可靠
        ExtractTextElements(page, elements);
        
        // 提取图片
        ExtractImageElements(page, elements);
        
        // 简化的表格检测（基于文本布局模式）
        ExtractSimpleTableElements(page, elements);

        // 按垂直位置排序并转换为 DocumentBlock
        int currentOrder = startOrder;
        foreach (var element in elements.OrderBy(e => e.VerticalPosition))
        {
            var block = ConvertToDocumentBlock(element, currentOrder++);
            if (block != null)
                result.Add(block);
        }

        return result;
    }

    /// <summary>
    /// 提取文本元素
    /// </summary>
    private void ExtractTextElements(PdfPage page, List<PageElement> elements)
    {
        try
        {
            var pageText = page.Text;
            if (string.IsNullOrWhiteSpace(pageText)) return;

            var cleanText = NormalizeText(pageText);
            var paragraphs = SplitIntoParagraphs(cleanText);
            
            var letters = page.Letters.ToList();
            double currentVerticalPos = 0;

            for (int i = 0; i < paragraphs.Count; i++)
            {
                var paragraph = paragraphs[i];
                if (paragraph.Length < MinParagraphLength) continue;

                // 简化的位置估算
                currentVerticalPos = EstimateVerticalPosition(i, paragraphs.Count, letters);

                elements.Add(new PageElement
                {
                    Type = ElementType.Text,
                    Content = paragraph,
                    VerticalPosition = currentVerticalPos
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Text extraction failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 提取图片元素
    /// </summary>
    private void ExtractImageElements(PdfPage page, List<PageElement> elements)
    {
        try
        {
            var images = page.GetImages();
            foreach (var img in images)
            {
                var imageBytes = ExtractImageBytes(img);
                if (imageBytes == null || imageBytes.Length < MinImageSize) continue;

                var verticalPos = CalculateImageVerticalPosition(img, page);
                
                elements.Add(new PageElement
                {
                    Type = ElementType.Image,
                    ImageData = imageBytes,
                    VerticalPosition = verticalPos
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Image extraction failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 简化的表格检测 - 基于文本行的列对齐模式
    /// </summary>
    private void ExtractSimpleTableElements(PdfPage page, List<PageElement> elements)
    {
        try
        {
            var words = ExtractWordsWithPositions(page);
            if (words.Count < 8) return; // 太少单词不可能形成表格

            var potentialTableRows = DetectTableRows(words);
            if (potentialTableRows.Count >= 2) // 至少2行才认为是表格
            {
                var tableData = ConvertToTableData(potentialTableRows);
                var avgVerticalPos = potentialTableRows.Average(row => row.Average(w => w.Y));

                elements.Add(new PageElement
                {
                    Type = ElementType.Table,
                    TableData = tableData,
                    VerticalPosition = avgVerticalPos
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Table detection failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 标准化文本
    /// </summary>
    private string NormalizeText(string text)
    {
        // 标准化换行符和清理多余空白
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        
        var lines = text.Split('\n')
            .Select(line => WhitespaceNormalizer.Replace(line.Trim(), " "))
            .Where(line => !string.IsNullOrWhiteSpace(line));
            
        return string.Join('\n', lines);
    }

    /// <summary>
    /// 分割段落
    /// </summary>
    private List<string> SplitIntoParagraphs(string text)
    {
        return ParagraphSeparator.Split(text)
            .Select(p => p.Trim())
            .Where(p => p.Length >= MinParagraphLength)
            .ToList();
    }

    /// <summary>
    /// 估算垂直位置
    /// </summary>
    private double EstimateVerticalPosition(int paragraphIndex, int totalParagraphs, List<Letter> letters)
    {
        if (letters.Count == 0) return paragraphIndex * 100;
        
        // 基于段落索引和页面高度估算
        var progress = totalParagraphs > 1 ? (double)paragraphIndex / (totalParagraphs - 1) : 0;
        var pageHeight = letters.Max(l => l.GlyphRectangle.Bottom) - letters.Min(l => l.GlyphRectangle.Bottom);
        
        return letters.Min(l => l.GlyphRectangle.Bottom) + progress * pageHeight;
    }

    /// <summary>
    /// 计算图片垂直位置
    /// </summary>
    private double CalculateImageVerticalPosition(IPdfImage image, PdfPage page)
    {
        try
        {
            var bounds = image.Bounds;
            return bounds.Bottom;
        }
        catch
        {
            return page.Height * 0.5;
        }
    }

    /// <summary>
    /// 提取带位置信息的单词
    /// </summary>
    private List<PositionedWord> ExtractWordsWithPositions(PdfPage page)
    {
        var words = new List<PositionedWord>();
        var letters = page.Letters.ToList();
        
        if (letters.Count == 0) return words;

        var currentWord = new StringBuilder();
        var wordBounds = new List<Letter>();

        foreach (var letter in letters.OrderByDescending(l => l.GlyphRectangle.Bottom)
                                     .ThenBy(l => l.GlyphRectangle.Left))
        {
            bool shouldStartNewWord = ShouldStartNewWord(letter, wordBounds.LastOrDefault());
            
            if (shouldStartNewWord && currentWord.Length > 0)
            {
                AddCompletedWord(words, currentWord, wordBounds);
                currentWord.Clear();
                wordBounds.Clear();
            }
            
            currentWord.Append(letter.Value);
            wordBounds.Add(letter);
        }

        // 处理最后一个单词
        if (currentWord.Length > 0)
        {
            AddCompletedWord(words, currentWord, wordBounds);
        }

        return words;
    }

    /// <summary>
    /// 判断是否应该开始新单词
    /// </summary>
    private bool ShouldStartNewWord(Letter currentLetter, Letter? lastLetter)
    {
        if (lastLetter == null) return false;
        
        var xGap = currentLetter.GlyphRectangle.Left - lastLetter.GlyphRectangle.Right;
        var yGap = Math.Abs(currentLetter.GlyphRectangle.Bottom - lastLetter.GlyphRectangle.Bottom);
        
        return xGap > 8 || yGap > LineHeightTolerance; // 字符间距或行高超过阈值
    }

    /// <summary>
    /// 添加完成的单词
    /// </summary>
    private void AddCompletedWord(List<PositionedWord> words, StringBuilder wordText, List<Letter> wordBounds)
    {
        var text = wordText.ToString().Trim();
        if (text.Length == 0) return;

        var bounds = wordBounds;
        var x = bounds.Min(l => l.GlyphRectangle.Left);
        var y = bounds.Average(l => l.GlyphRectangle.Bottom);
        
        words.Add(new PositionedWord { Text = text, X = x, Y = y });
    }

    /// <summary>
    /// 检测表格行
    /// </summary>
    private List<List<PositionedWord>> DetectTableRows(List<PositionedWord> words)
    {
        // 按Y坐标分组成行
        var rowGroups = words
            .GroupBy(w => Math.Round(w.Y / LineHeightTolerance) * LineHeightTolerance)
            .Where(g => g.Count() >= 2) // 至少2个单词才能形成行
            .OrderByDescending(g => g.Key)
            .Take(20) // 限制检测行数
            .ToList();

        if (rowGroups.Count < 2) return new List<List<PositionedWord>>();

        // 检测列对齐
        var columnPositions = DetectColumnAlignment(rowGroups);
        if (columnPositions.Count < 2) return new List<List<PositionedWord>>();

        // 构建表格行
        var tableRows = new List<List<PositionedWord>>();
        foreach (var rowGroup in rowGroups)
        {
            var row = AlignWordsToColumns(rowGroup.ToList(), columnPositions);
            if (row.Count >= 2)
            {
                tableRows.Add(row);
            }
        }

        return tableRows;
    }

    /// <summary>
    /// 检测列对齐
    /// </summary>
    private List<double> DetectColumnAlignment(List<IGrouping<double, PositionedWord>> rowGroups)
    {
        var allXPositions = rowGroups.SelectMany(g => g.Select(w => w.X)).ToList();
        var sortedPositions = allXPositions.OrderBy(x => x).ToList();
        
        var columns = new List<double>();
        double? lastColumn = null;
        
        foreach (var pos in sortedPositions)
        {
            if (lastColumn == null || pos - lastColumn > ColumnAlignmentTolerance)
            {
                columns.Add(pos);
                lastColumn = pos;
            }
        }
        
        return columns.Take(8).ToList(); // 最多8列
    }

    /// <summary>
    /// 将单词对齐到列
    /// </summary>
    private List<PositionedWord> AlignWordsToColumns(List<PositionedWord> rowWords, List<double> columnPositions)
    {
        var result = new List<PositionedWord>();
        var availableWords = rowWords.ToList();
        
        foreach (var colPos in columnPositions)
        {
            var nearestWord = availableWords
                .Where(w => Math.Abs(w.X - colPos) <= ColumnAlignmentTolerance)
                .OrderBy(w => Math.Abs(w.X - colPos))
                .FirstOrDefault();
                
            if (nearestWord != null)
            {
                result.Add(nearestWord);
                availableWords.Remove(nearestWord);
            }
            else
            {
                result.Add(new PositionedWord { Text = "", X = colPos, Y = 0 });
            }
        }
        
        return result;
    }

    /// <summary>
    /// 转换为表格数据
    /// </summary>
    private List<List<string>> ConvertToTableData(List<List<PositionedWord>> tableRows)
    {
        return tableRows.Select(row => row.Select(word => word.Text).ToList()).ToList();
    }

    /// <summary>
    /// 提取图片字节数据
    /// </summary>
    private byte[]? ExtractImageBytes(IPdfImage image)
    {
        try
        {
            var bytes = image.RawBytes;
            return bytes.Length > 0 ? bytes.ToArray() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 转换为 DocumentBlock
    /// </summary>
    private DocumentBlock? ConvertToDocumentBlock(PageElement element, int order)
    {
        return element.Type switch
        {
            ElementType.Text => new DocumentBlock
            {
                Type = DocumentBlockType.Text,
                Text = element.Content,
                Order = order
            },
            
            ElementType.Image when element.ImageData != null => new DocumentBlock
            {
                Type = DocumentBlockType.Image,
                ImageBytes = element.ImageData,
                Order = order
            },
            
            ElementType.Table when element.TableData != null && element.TableData.Count > 0 => 
                CreateTableBlock(element.TableData, order),
            
            _ => null
        };
    }

    /// <summary>
    /// 创建表格块
    /// </summary>
    private DocumentBlock CreateTableBlock(List<List<string>> tableData, int order)
    {
        var markdown = GenerateTableMarkdown(tableData);
        var hash = GenerateTableHash(tableData);
        var caption = GenerateTableCaption(tableData);
        
        return new DocumentBlock
        {
            Type = DocumentBlockType.Table,
            Order = order,
            TableRows = tableData.Select(row => (IReadOnlyList<string>)row.AsReadOnly()).ToList(),
            TableMarkdown = markdown,
            TableCaption = caption,
            TableHash = hash,
            Text = markdown,
            Caption = caption
        };
    }

    /// <summary>
    /// 生成表格 Markdown
    /// </summary>
    private string GenerateTableMarkdown(List<List<string>> tableData)
    {
        if (tableData.Count == 0) return string.Empty;
        
        var sb = new StringBuilder();
        sb.AppendLine("[TABLE]");
        
        var header = tableData[0];
        sb.Append("| ").AppendJoin(" | ", header.Select(EscapeMarkdown)).AppendLine(" |");
        sb.Append("| ").AppendJoin(" | ", header.Select(_ => "---")).AppendLine(" |");
        
        foreach (var row in tableData.Skip(1))
        {
            sb.Append("| ").AppendJoin(" | ", row.Select(EscapeMarkdown)).AppendLine(" |");
        }
        
        return sb.ToString();
    }

    /// <summary>
    /// 生成表格哈希
    /// </summary>
    private string GenerateTableHash(List<List<string>> tableData)
    {
        var content = string.Join('|', tableData.Select(row => string.Join('~', row)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }

    /// <summary>
    /// 生成表格标题
    /// </summary>
    private string? GenerateTableCaption(List<List<string>> tableData)
    {
        if (tableData.Count == 0) return null;
        
        var firstRow = tableData[0];
        return firstRow.Count <= 6 && firstRow.All(cell => cell.Length <= 20 && !string.IsNullOrWhiteSpace(cell))
            ? string.Join(" | ", firstRow.Where(c => !string.IsNullOrWhiteSpace(c)))
            : null;
    }

    /// <summary>
    /// 转义 Markdown 字符
    /// </summary>
    private string EscapeMarkdown(string text) => 
        text.Replace("|", "\\|").Replace("\n", "<br>").Replace("\r", "");

    #region 内部类型定义

    private enum ElementType
    {
        Text,
        Image,
        Table
    }

    private class PageElement
    {
        public ElementType Type { get; set; }
        public string Content { get; set; } = string.Empty;
        public byte[]? ImageData { get; set; }
        public List<List<string>>? TableData { get; set; }
        public double VerticalPosition { get; set; }
    }

    private class PositionedWord
    {
        public string Text { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
    }

    #endregion
}
