using MarketAssistant.Rag.Interfaces;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using PdfPage = UglyToad.PdfPig.Content.Page;

namespace MarketAssistant.Rag.Services;

/// <summary>
/// PDF到Markdown转换器
/// 使用UglyToad.PdfPig提取文本并转换为Markdown格式
/// 支持标题识别、段落处理、列表识别、表格处理和图片引用
/// </summary>
public class PdfMarkdownConverter : IMarkdownConverter
{
    // 编译的正则表达式，提高性能
    private static readonly Regex MultipleSpacesRegex = new(@"\s{2,}", RegexOptions.Compiled);
    private static readonly Regex MultipleNewlinesRegex = new(@"\n{3,}", RegexOptions.Compiled);
    private static readonly Regex NumberedListRegex = new(@"^\s*(\d+\.|\d+\)|\(\d+\))\s+", RegexOptions.Compiled);
    private static readonly Regex BulletListRegex = new(@"^\s*[•\-\*\◦\▪\▫]\s+", RegexOptions.Compiled);
    private static readonly Regex ChapterNumberRegex = new(@"^\s*(第[一二三四五六七八九十\d]+章|Chapter\s+\d+|CHAPTER\s+\d+)\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SectionNumberRegex = new(@"^\s*(\d+(\.\d+)*)\s+", RegexOptions.Compiled);
    private static readonly Regex ChineseSectionRegex = new(@"^\s*([一二三四五六七八九十]|[1-9]\d*)、", RegexOptions.Compiled);
    private static readonly Regex ChineseSubSectionRegex = new(@"^\s*（([一二三四五六七八九十]|[1-9]\d*)）", RegexOptions.Compiled);
    private static readonly Regex TablePatternRegex = new(@"^\s*\|.*\|\s*$", RegexOptions.Compiled);

    // 字体大小阈值
    private const double LargeHeadingThreshold = 1.5;
    private const double MediumHeadingThreshold = 1.3;
    private const double SmallHeadingThreshold = 1.1;

    // 表格检测参数
    private const int MinTableColumns = 2;

    private readonly IImageStorageService _imageStorageService;

    public PdfMarkdownConverter(IImageStorageService imageStorageService)
    {
        _imageStorageService = imageStorageService ?? throw new ArgumentNullException(nameof(imageStorageService));
    }

    public bool CanConvert(string filePath) =>
        !string.IsNullOrEmpty(filePath) && filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    public async Task<string> ConvertToMarkdownAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"PDF文件不存在: {filePath}");
        }

        try
        {
            return await ConvertPdfToMarkdown(filePath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"转换PDF文件失败: {ex.Message}", ex);
        }
    }

    private async Task<string> ConvertPdfToMarkdown(string filePath)
    {
        using var document = PdfDocument.Open(filePath);
        var markdownBuilder = new StringBuilder();
        var documentInfo = AnalyzeDocument(document);

        for (int pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
        {
            try
            {
                var page = document.GetPage(pageNumber);
                await ProcessPage(page, markdownBuilder, documentInfo, pageNumber, filePath);
            }
            catch (Exception ex)
            {
                // 记录错误但继续处理其他页面
                System.Diagnostics.Debug.WriteLine($"处理第{pageNumber}页时出错: {ex.Message}");
                markdownBuilder.AppendLine($"\n<!-- 第{pageNumber}页处理失败 -->\n");
            }
        }

        return PostProcessMarkdown(markdownBuilder.ToString());
    }

    private DocumentInfo AnalyzeDocument(PdfDocument document)
    {
        var allFontSizes = new List<double>();
        var headingCandidates = new List<HeadingCandidate>();
        var wordCount = 0;

        // 分析整个文档以确定字体大小分布和潜在标题
        for (int pageNumber = 1; pageNumber <= Math.Min(document.NumberOfPages, 5); pageNumber++) // 只分析前5页以提高性能
        {
            try
            {
                var page = document.GetPage(pageNumber);
                var words = page.GetWords();
                var lines = ExtractStructuredLines(page, new DocumentInfo { AverageFontSize = 12.0, MaxFontSize = 24.0, MinFontSize = 8.0, TotalWords = 0 });

                foreach (var word in words)
                {
                    if (word.Letters.Any())
                    {
                        var fontSize = word.Letters.First().FontSize;
                        allFontSizes.Add(fontSize);
                        wordCount++;
                    }
                }

                // 收集潜在的标题候选
                foreach (var line in lines)
                {
                    var text = line.Text.Trim();
                    if (!string.IsNullOrEmpty(text) && text.Length <= 100 && !text.EndsWith('.'))
                    {
                        var fontSizeRatio = line.FontSize / (allFontSizes.Any() ? allFontSizes.Average() : 12.0);
                        if (fontSizeRatio >= 1.1 || line.IsBold ||
                            ChapterNumberRegex.IsMatch(text) ||
                            ChineseSectionRegex.IsMatch(text) ||
                            ChineseSubSectionRegex.IsMatch(text) ||
                            SectionNumberRegex.IsMatch(text))
                        {
                            headingCandidates.Add(new HeadingCandidate
                            {
                                Text = text,
                                FontSize = line.FontSize,
                                IsBold = line.IsBold,
                                PageNumber = pageNumber
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"分析第{pageNumber}页时出错: {ex.Message}");
            }
        }

        var avgFontSize = allFontSizes.Any() ? allFontSizes.Average() : 12.0;
        var maxFontSize = allFontSizes.Any() ? allFontSizes.Max() : 12.0;
        var minFontSize = allFontSizes.Any() ? allFontSizes.Min() : 12.0;

        // 分析标题级别分布，确定动态阈值
        var headingThresholds = CalculateDynamicThresholds(headingCandidates, avgFontSize);

        return new DocumentInfo
        {
            AverageFontSize = avgFontSize,
            MaxFontSize = maxFontSize,
            MinFontSize = minFontSize,
            TotalWords = wordCount,
            HeadingCandidates = headingCandidates,
            DynamicThresholds = headingThresholds
        };
    }

    private async Task ProcessPage(PdfPage page, StringBuilder markdown, DocumentInfo docInfo, int pageNumber, string filePath)
    {
        try
        {
            var structuredLines = ExtractStructuredLines(page, docInfo);

            // 检测表格
            var tables = DetectTables(structuredLines);

            // 处理结构化内容
            ProcessStructuredContent(structuredLines, tables, markdown, docInfo);

            // 处理图片
            await ProcessPageImages(page, markdown, pageNumber, filePath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"处理页面内容时出错: {ex.Message}");
            // 降级到简单文本提取
            var fallbackText = page.Text;
            if (!string.IsNullOrWhiteSpace(fallbackText))
            {
                markdown.AppendLine(CleanText(fallbackText));
                markdown.AppendLine();
            }
        }
    }

    private List<StructuredLine> ExtractStructuredLines(PdfPage page, DocumentInfo docInfo)
    {
        var lines = new List<StructuredLine>();

        try
        {
            var words = page.GetWords().ToList();
            if (!words.Any()) return lines;

            // 按行分组单词
            var lineGroups = words
                .GroupBy(w => Math.Round(w.BoundingBox.Bottom, 1))
                .OrderByDescending(g => g.Key)
                .ToList();

            foreach (var lineGroup in lineGroups)
            {
                var lineWords = lineGroup
                    .OrderBy(w => w.BoundingBox.Left)
                    .ToList();

                if (!lineWords.Any()) continue;

                var lineText = string.Join(" ", lineWords.Select(w => w.Text));
                var leftMargin = lineWords.First().BoundingBox.Left;

                // 获取字体信息
                var fontSize = docInfo.AverageFontSize;
                var isBold = false;
                var isItalic = false;

                try
                {
                    var firstLetter = lineWords.FirstOrDefault()?.Letters?.FirstOrDefault();
                    if (firstLetter != null)
                    {
                        fontSize = firstLetter.FontSize;
                        var fontName = firstLetter.FontName ?? "";
                        isBold = fontName.Contains("Bold", StringComparison.OrdinalIgnoreCase);
                        isItalic = fontName.Contains("Italic", StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch
                {
                    // 使用默认值
                }

                lines.Add(new StructuredLine
                {
                    Text = lineText,
                    FontSize = fontSize,
                    IsBold = isBold,
                    IsItalic = isItalic,
                    LeftMargin = leftMargin,
                    TopPosition = lineGroup.Key,
                    BoundingBox = new BoundingBox
                    {
                        Left = lineWords.Min(w => w.BoundingBox.Left),
                        Right = lineWords.Max(w => w.BoundingBox.Right),
                        Top = lineWords.Max(w => w.BoundingBox.Top),
                        Bottom = lineWords.Min(w => w.BoundingBox.Bottom)
                    }
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"提取结构化行时出错: {ex.Message}");
        }

        return lines;
    }

    private List<TableInfo> DetectTables(List<StructuredLine> lines)
    {
        var tables = new List<TableInfo>();
        if (lines.Count == 0) return tables;

        // 先为每一行基于词间距拆分潜在单元格
        var lineCellInfos = new List<LineCells>();
        for (int i = 0; i < lines.Count; i++)
        {
            lineCellInfos.Add(SplitLineIntoCells(lines[i]));
        }

        bool IsLikelyTableLine(LineCells lc)
        {
            if (lc.Cells.Count < MinTableColumns) return false;
            // 平均单元格长度（字符）
            var avgLen = lc.Cells.Average(c => c.Text.Length);
            if (avgLen > 60) return false; // 太长可能是段落
            // 含有数字或列数≥3 更倾向于表格
            bool hasDigit = lc.Cells.Any(c => c.Text.Any(char.IsDigit));
            return hasDigit || lc.Cells.Count >= 3;
        }

        int idx = 0;
        while (idx < lines.Count)
        {
            if (!IsLikelyTableLine(lineCellInfos[idx])) { idx++; continue; }

            int start = idx;
            int lastTableLine = idx;
            int gapAllowance = 1; // 允许夹1行非表格（多行单元格内容）
            int gaps = 0;
            var candidateLineCells = new List<(int index, LineCells cells)>();

            while (idx < lines.Count)
            {
                var lc = lineCellInfos[idx];
                if (IsLikelyTableLine(lc))
                {
                    candidateLineCells.Add((idx, lc));
                    lastTableLine = idx;
                    gaps = 0;
                    idx++;
                }
                else if (gaps < gapAllowance)
                {
                    // 可能是前一单元格的续行，先暂存（不直接作为表格解析行）
                    gaps++;
                    idx++;
                }
                else
                {
                    break;
                }
            }

            if (candidateLineCells.Count >= 2)
            {
                var tableLines = candidateLineCells.Select(c => lines[c.index]).ToList();
                var rowIndices = candidateLineCells.Select(c => c.index).ToList();
                tables.Add(new TableInfo
                {
                    Rows = tableLines,
                    StartIndex = rowIndices.Min(),
                    EndIndex = rowIndices.Max(),
                    ActualRowIndices = rowIndices
                });
            }
            else
            {
                // 不足以构成表格，回退一个位置继续
                idx = start + 1;
            }
        }

        return tables;
    }

    // ========== 新的通用表格解析辅助结构与函数 ==========

    private record CellFragment(double Left, string Text);
    private class LineCells
    {
        public StructuredLine Line { get; init; } = null!;
        public List<CellFragment> Cells { get; init; } = new();
    }

    private LineCells SplitLineIntoCells(StructuredLine line)
    {
        // 基于多个空格或明显的水平间隔（Left差值）来拆分
        var words = line.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cells = new List<CellFragment>();
        if (words.Length == 0)
        {
            return new LineCells { Line = line, Cells = cells };
        }

        // 由于我们没有逐词坐标（为保持侵入性最小，不修改上层结构），用多空格估计分列
        // 如果后续需要更精准，可在 StructuredLine 中保留 word 级坐标。
        var raw = line.Text;
        // 使用两个及以上空格或制表符作为列分隔符
        var split = Regex.Split(raw.Trim(), @"(\s{2,}|\t+)").Where(s => !string.IsNullOrWhiteSpace(s) && !Regex.IsMatch(s, @"^\s{2,}$")).ToList();
        if (split.Count <= 1)
        {
            // 回退：用单空格分但只取>2列的情况
            if (words.Length >= 3)
            {
                split = words.ToList();
            }
        }
        double currentLeft = line.LeftMargin;
        foreach (var s in split)
        {
            cells.Add(new CellFragment(currentLeft, CleanText(s)));
            currentLeft += 50; // 人工递增，后面列对齐时只用相对顺序
        }
        return new LineCells { Line = line, Cells = cells };
    }

    private void ProcessStructuredContent(List<StructuredLine> lines, List<TableInfo> tables, StringBuilder markdown, DocumentInfo docInfo)
    {
        var processedIndices = new HashSet<int>();

        // 调试输出：显示检测到的表格
        if (tables.Any())
        {
            Console.WriteLine($"检测到 {tables.Count} 个表格:");
            foreach (var table in tables)
            {
                Console.WriteLine($"  表格 {tables.IndexOf(table) + 1}: 行 {table.StartIndex}-{table.EndIndex}, 共 {table.Rows.Count} 行");
                foreach (var row in table.Rows)
                {
                    Console.WriteLine($"    行内容: {row.Text.Trim()}");
                }
            }
        }
        else
        {
            Console.WriteLine("未检测到任何表格");
        }

        // 改进的处理逻辑：按顺序处理所有行，确保表格和文本都正确处理
        for (int i = 0; i < lines.Count; i++)
        {
            if (processedIndices.Contains(i)) continue;

            var line = lines[i];
            var cleanText = CleanText(line.Text);

            if (string.IsNullOrWhiteSpace(cleanText)) continue;

            // 检查是否是表格的开始
            var table = tables.FirstOrDefault(t => i == t.StartIndex);
            if (table != null)
            {
                Console.WriteLine($"处理表格，索引范围: {table.StartIndex} - {table.EndIndex}, 实际表格行: {string.Join(",", table.ActualRowIndices)}");
                ProcessTable(table, markdown);

                // 只标记实际的表格行为已处理，避免错误排除正常文本
                foreach (var tableRowIndex in table.ActualRowIndices)
                {
                    processedIndices.Add(tableRowIndex);
                }
                continue;
            }

            // 如果当前行是某个表格的实际行（不再使用StartIndex/EndIndex范围），跳过
            if (tables.Any(t => t.ActualRowIndices.Contains(i)))
            {
                processedIndices.Add(i);
                continue;
            }

            // 标题检测
            if (IsHeading(line, docInfo))
            {
                var level = DetermineHeadingLevel(line, docInfo);
                markdown.AppendLine($"{new string('#', level)} {cleanText}");
                markdown.AppendLine();
            }
            // 列表检测
            else if (IsNumberedList(cleanText))
            {
                var match = NumberedListRegex.Match(cleanText);
                var originalNumber = match.Groups[1].Value; // 提取原始编号（如 "1.", "2)", "(3)" 等）
                var listText = NumberedListRegex.Replace(cleanText, "").Trim();
                var indent = CalculateIndentLevel(line.LeftMargin, lines);

                // 规范化编号格式为标准的 "数字." 格式
                var numberOnly = Regex.Replace(originalNumber, @"[^\d]", "");
                markdown.AppendLine($"{new string(' ', indent * 2)}{numberOnly}. {listText}");
            }
            else if (IsBulletList(cleanText))
            {
                var listText = BulletListRegex.Replace(cleanText, "").Trim();
                var indent = CalculateIndentLevel(line.LeftMargin, lines);
                markdown.AppendLine($"{new string(' ', indent * 2)}- {listText}");
            }
            // 普通段落
            else
            {
                // 检查是否应该与前一行合并
                if (ShouldMergeWithPrevious(line, lines, i))
                {
                    markdown.Append($" {cleanText}");
                }
                else
                {
                    markdown.AppendLine();
                    markdown.AppendLine(cleanText);
                }
            }

            processedIndices.Add(i);
        }
    }

    private void ProcessTable(TableInfo table, StringBuilder markdown)
    {
        if (table.Rows.Count == 0) return;

        Console.WriteLine($"处理表格: {table.Rows.Count} 行");
        foreach (var row in table.Rows)
        {
            Console.WriteLine($"  表格行: '{row.Text}'");
        }

        markdown.AppendLine();

        // 重新分析表格结构 - 更智能的方法
        var tableData = AnalyzeAndRestructureTable(table.Rows);

        if (tableData.Count == 0)
        {
            Console.WriteLine("无法识别表格结构，作为普通文本处理");
            // 如果无法识别为表格，作为普通段落处理
            foreach (var row in table.Rows)
            {
                markdown.AppendLine(CleanText(row.Text));
            }
            markdown.AppendLine();
            return;
        }

        // 生成Markdown表格
        for (int i = 0; i < tableData.Count; i++)
        {
            var row = tableData[i];
            markdown.AppendLine($"| {string.Join(" | ", row)} |");

            // 在第一行后添加分隔行
            if (i == 0)
            {
                var separatorRow = "| " + string.Join(" | ", Enumerable.Repeat("---", row.Count)) + " |";
                markdown.AppendLine(separatorRow);
            }
        }

        markdown.AppendLine();
        Console.WriteLine($"表格处理完成，生成 {tableData.Count} 行");
    }

    private List<List<string>> AnalyzeAndRestructureTable(List<StructuredLine> rows)
    {
        // 通用：用 SplitLineIntoCells 重新对齐列，选择出现频率最高的列数
        var lineCells = rows.Select(SplitLineIntoCells).ToList();
        var columnCounts = lineCells.Where(lc => lc.Cells.Count >= MinTableColumns).Select(lc => lc.Cells.Count).ToList();
        if (!columnCounts.Any()) return new List<List<string>>();
        int targetColumns = columnCounts
            .GroupBy(c => c)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => g.Key)
            .First().Key;

        var table = new List<List<string>>();

        foreach (var lc in lineCells)
        {
            if (lc.Cells.Count < 1) continue;
            if (lc.Cells.Count == targetColumns)
            {
                table.Add(lc.Cells.Select(c => c.Text).ToList());
            }
            else if (lc.Cells.Count > targetColumns)
            {
                // 合并多余列（从右向左合并最短文本）
                var cells = lc.Cells.Select(c => c.Text).ToList();
                while (cells.Count > targetColumns)
                {
                    // 找到两个最短相邻合并
                    int mergeIndex = 0;
                    int minLen = int.MaxValue;
                    for (int i = 0; i < cells.Count - 1; i++)
                    {
                        int lens = cells[i].Length + cells[i + 1].Length;
                        if (lens < minLen)
                        {
                            minLen = lens;
                            mergeIndex = i;
                        }
                    }
                    cells[mergeIndex] = (cells[mergeIndex] + " " + cells[mergeIndex + 1]).Trim();
                    cells.RemoveAt(mergeIndex + 1);
                }
                table.Add(cells);
            }
            else // 少于目标列，尝试用空列填充（多行单元格可能导致）
            {
                var cells = lc.Cells.Select(c => c.Text).ToList();
                while (cells.Count < targetColumns) cells.Add("");
                table.Add(cells);
            }
        }

        // 尝试识别表头：第一行如果所有列都是非数字且长度适中
        if (table.Count > 1)
        {
            bool firstRowHeader = table[0].Count(c => c.Any(char.IsLetter)) >= Math.Max(2, targetColumns - 1) &&
                                  table[0].Any(c => c.Contains("名称") || c.Contains("地区") || c.Contains("时间") || c.Contains("类") || c.Contains("种"));
            if (!firstRowHeader)
            {
                // 生成一个通用表头
                var header = new List<string>();
                for (int i = 0; i < targetColumns; i++) header.Add($"列{i + 1}");
                table.Insert(0, header);
            }
        }
        return table;
    }

    // 旧的特定案例处理逻辑已移除，保留最小必要工具函数
    private bool IsDataRow(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Count(w => w.Any(char.IsDigit)) >= 2;

    private async Task ProcessPageImages(PdfPage page, StringBuilder markdown, int pageNumber, string filePath)
    {
        try
        {
            var images = page.GetImages();
            var imageCount = 0;

            foreach (var image in images)
            {
                imageCount++;
                var altText = $"页面{pageNumber}图片{imageCount}";

                // 生成标准的图片文件名
                var imageFileName = $"page{pageNumber}_image{imageCount}.png";

                try
                {
                    // 提取图片字节数据
                    var imageBytes = ExtractImageBytes(image);
                    if (imageBytes != null && imageBytes.Length > 0)
                    {
                        // 保存图片
                        var imagePath = await _imageStorageService.SaveImageAsync(imageBytes, imageFileName, filePath);
                        var relativeImagePath = Path.GetRelativePath(Path.GetDirectoryName(filePath)!, imagePath);
                        
                        markdown.AppendLine();
                        markdown.AppendLine($"![{altText}]({relativeImagePath})");
                        markdown.AppendLine();
                    }
                    else
                    {
                        // 无法提取图片，使用占位符
                        markdown.AppendLine();
                        markdown.AppendLine($"![{altText}](图片占位符: {imageFileName})");
                        markdown.AppendLine();
                    }
                }
                catch (Exception imgEx)
                {
                    System.Diagnostics.Debug.WriteLine($"提取第{pageNumber}页图片{imageCount}时出错: {imgEx.Message}");
                    // 提取失败，使用占位符
                    markdown.AppendLine();
                    markdown.AppendLine($"![{altText}](图片提取失败: {imageFileName})");
                    markdown.AppendLine();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"处理第{pageNumber}页图片时出错: {ex.Message}");
        }
    }

    private byte[]? ExtractImageBytes(UglyToad.PdfPig.Content.IPdfImage image)
    {
        try
        {
            // 检查图片是否有原始字节数据
            var rawBytes = image.RawBytes;
            if (rawBytes.Length > 0)
            {
                return rawBytes.ToArray();
            }

            // 如果没有原始字节数据，尝试从其他属性获取
            // 注意：这里可能需要根据不同的图片格式进行特殊处理
            // 对于复杂的PDF图片提取，可能需要更高级的处理逻辑

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"提取图片字节数据时出错: {ex.Message}");
            return null;
        }
    }

    private bool IsHeading(StructuredLine line, DocumentInfo docInfo)
    {
        var text = line.Text.Trim();
        if (string.IsNullOrEmpty(text)) return false;

        // 检查章节编号
        if (ChapterNumberRegex.IsMatch(text) || SectionNumberRegex.IsMatch(text) ||
            ChineseSectionRegex.IsMatch(text) || ChineseSubSectionRegex.IsMatch(text))
            return true;

        // 检查字体大小
        var fontSizeRatio = line.FontSize / docInfo.AverageFontSize;
        if (fontSizeRatio >= docInfo.DynamicThresholds.Level3Threshold)
        {
            // 附加条件：较短的文本，不以句号结尾，首字符是字母、数字或中文字符
            return text.Length <= 100 &&
                   !text.EndsWith('.') &&
                   (char.IsUpper(text[0]) || char.IsLetter(text[0]) || char.IsDigit(text[0]) || IsChinese(text[0])) &&
                   text.Split(' ').Length <= 10;
        }

        // 检查粗体
        if (line.IsBold && text.Length <= 100 && !text.EndsWith('.'))
            return true;

        return false;
    }

    private static bool IsChinese(char c)
    {
        // 检查是否为中文字符（CJK统一汉字）
        return c >= 0x4E00 && c <= 0x9FFF ||  // CJK Unified Ideographs
               c >= 0x3400 && c <= 0x4DBF ||  // CJK Extension A
               c >= 0x20000 && c <= 0x2A6DF;  // CJK Extension B
    }

    private int DetermineHeadingLevel(StructuredLine line, DocumentInfo docInfo)
    {
        var text = line.Text.Trim();
        var fontSizeRatio = line.FontSize / docInfo.AverageFontSize;
        var thresholds = docInfo.DynamicThresholds;

        // 1. 最高优先级：章节编号 (第X章)
        if (ChapterNumberRegex.IsMatch(text))
            return 1;

        // 2. 高优先级：明确的编号格式
        // 中文主要章节编号 (一、二、三、...)
        if (ChineseSectionRegex.IsMatch(text))
        {
            // 如果字体很大，可能是一级标题，否则是二级
            return fontSizeRatio >= thresholds.Level1Threshold ? 1 : 2;
        }

        // 中文子章节编号 (（一）、（二）、...)
        if (ChineseSubSectionRegex.IsMatch(text))
            return 3;

        // 阿拉伯数字编号 (1.1, 1.2, ...)
        if (SectionNumberRegex.IsMatch(text))
        {
            var match = SectionNumberRegex.Match(text);
            var numberParts = match.Groups[1].Value.Split('.');
            return Math.Min(numberParts.Length, 6);
        }

        // 3. 基于字体大小的智能判断
        if (fontSizeRatio >= thresholds.Level1Threshold)
        {
            return 1;
        }
        if (fontSizeRatio >= thresholds.Level2Threshold)
        {
            return 2;
        }
        if (fontSizeRatio >= thresholds.Level3Threshold)
        {
            return 3;
        }

        // 4. 基于格式的判断
        if (line.IsBold)
        {
            // 粗体文本，根据字体大小确定级别
            if (fontSizeRatio >= 1.2) return 3;
            return 4;
        }

        // 5. 默认较低级别
        return 5;
    }

    private bool IsNumberedList(string text) => NumberedListRegex.IsMatch(text);

    private bool IsBulletList(string text) => BulletListRegex.IsMatch(text);

    private int CalculateIndentLevel(double leftMargin, List<StructuredLine> allLines)
    {
        if (!allLines.Any()) return 0;

        var avgMargin = allLines.Average(l => l.LeftMargin);
        var marginDiff = leftMargin - avgMargin;

        return Math.Max(0, (int)(marginDiff / 20)); // 每20个单位为一个缩进级别
    }

    private bool ShouldMergeWithPrevious(StructuredLine current, List<StructuredLine> lines, int currentIndex)
    {
        if (currentIndex == 0) return false;

        var previous = lines[currentIndex - 1];
        var currentText = current.Text.Trim();
        var previousText = previous.Text.Trim();

        // 如果当前行以小写字母开始，且前一行不以句号结尾，可能是同一段落
        if (char.IsLower(currentText[0]) && !previousText.EndsWith('.') && !previousText.EndsWith(':'))
        {
            // 检查字体大小是否相似
            var fontSizeDiff = Math.Abs(current.FontSize - previous.FontSize);
            if (fontSizeDiff < 2.0)
            {
                return true;
            }
        }

        return false;
    }

    private string CleanText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // 移除多余的空格
        text = MultipleSpacesRegex.Replace(text, " ");

        // 处理常见的PDF编码问题
        text = text
            .Replace("ﬁ", "fi")     // 连字符修复
            .Replace("ﬂ", "fl")
            .Replace("ﬀ", "ff")
            .Replace("ﬃ", "ffi")
            .Replace("ﬄ", "ffl")
            .Replace("–", "-")      // 短破折号
            .Replace("—", "--")     // 长破折号
            .Replace("\u201C", "\"") // 左双引号
            .Replace("\u201D", "\"") // 右双引号
            .Replace("\u2018", "'")  // 左单引号
            .Replace("\u2019", "'")  // 右单引号
            .Replace("\u2013", "-")  // 短破折号
            .Replace("\u2014", "--") // 长破折号
            .Replace("\u00A0", " ")  // 非断行空格
            .Trim();

        return text;
    }

    private string PostProcessMarkdown(string markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return string.Empty;

        // 移除过多的空行
        markdown = MultipleNewlinesRegex.Replace(markdown, "\n\n");

        // 修复格式问题
        var lines = markdown.Split('\n');
        var result = new StringBuilder();
        var previousLineWasEmpty = false;

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            // 标题前后确保有空行
            if (trimmedLine.StartsWith('#'))
            {
                if (!previousLineWasEmpty && result.Length > 0)
                {
                    result.AppendLine();
                }
                result.AppendLine(trimmedLine);
                result.AppendLine();
                previousLineWasEmpty = true;
            }
            // 列表项
            else if (trimmedLine.StartsWith("- ") || trimmedLine.StartsWith("1. ") ||
                     Regex.IsMatch(trimmedLine, @"^\s*\d+\.\s"))
            {
                result.AppendLine(trimmedLine);
                previousLineWasEmpty = false;
            }
            // 空行
            else if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                if (!previousLineWasEmpty)
                {
                    result.AppendLine();
                    previousLineWasEmpty = true;
                }
            }
            // 普通内容
            else
            {
                result.AppendLine(trimmedLine);
                previousLineWasEmpty = false;
            }
        }

        return result.ToString().Trim();
    }

    private DynamicThresholds CalculateDynamicThresholds(List<HeadingCandidate> candidates, double avgFontSize)
    {
        if (!candidates.Any())
        {
            return new DynamicThresholds();
        }

        // 计算字体大小比率
        var fontRatios = candidates
            .Select(c => c.FontSize / avgFontSize)
            .Where(r => r >= 1.05) // 只考虑明显大于平均字体的候选项
            .OrderByDescending(r => r)
            .Distinct()
            .ToList();

        if (fontRatios.Count == 0)
        {
            return new DynamicThresholds();
        }

        // 根据实际字体大小分布确定阈值
        var thresholds = new DynamicThresholds();

        if (fontRatios.Count == 1)
        {
            // 只有一个字体大小级别
            var ratio = fontRatios[0];
            thresholds = new DynamicThresholds
            {
                Level1Threshold = ratio,
                Level2Threshold = Math.Max(ratio - 0.2, SmallHeadingThreshold),
                Level3Threshold = SmallHeadingThreshold,
                FontSizeBreakpoints = new List<double> { ratio }
            };
        }
        else if (fontRatios.Count == 2)
        {
            // 两个字体大小级别
            thresholds = new DynamicThresholds
            {
                Level1Threshold = fontRatios[0],
                Level2Threshold = fontRatios[1],
                Level3Threshold = Math.Max(fontRatios[1] - 0.1, SmallHeadingThreshold),
                FontSizeBreakpoints = fontRatios
            };
        }
        else
        {
            // 多个字体大小级别，取前三个主要级别
            thresholds = new DynamicThresholds
            {
                Level1Threshold = fontRatios[0],
                Level2Threshold = fontRatios[1],
                Level3Threshold = fontRatios[2],
                FontSizeBreakpoints = fontRatios.Take(4).ToList()
            };
        }

        return thresholds;
    }

    // 数据结构定义
    private class DocumentInfo
    {
        public double AverageFontSize { get; init; }
        public double MaxFontSize { get; init; }
        public double MinFontSize { get; init; }
        public int TotalWords { get; init; }
        public List<HeadingCandidate> HeadingCandidates { get; init; } = new();
        public DynamicThresholds DynamicThresholds { get; init; } = new();
    }

    private class HeadingCandidate
    {
        public required string Text { get; init; }
        public double FontSize { get; init; }
        public bool IsBold { get; init; }
        public int PageNumber { get; init; }
    }

    private class DynamicThresholds
    {
        public double Level1Threshold { get; init; } = LargeHeadingThreshold;
        public double Level2Threshold { get; init; } = MediumHeadingThreshold;
        public double Level3Threshold { get; init; } = SmallHeadingThreshold;
        public List<double> FontSizeBreakpoints { get; init; } = new();
    }

    private class StructuredLine
    {
        public required string Text { get; init; }
        public double FontSize { get; init; }
        public bool IsBold { get; init; }
        public bool IsItalic { get; init; }
        public double LeftMargin { get; init; }
        public double TopPosition { get; init; }
        public BoundingBox BoundingBox { get; init; } = new();
    }

    private class TableInfo
    {
        public required List<StructuredLine> Rows { get; init; }
        public int StartIndex { get; init; }
        public int EndIndex { get; init; }
        public List<int> ActualRowIndices { get; init; } = new List<int>(); // 实际的表格行索引
    }

    private class BoundingBox
    {
        public double Left { get; init; }
        public double Right { get; init; }
        public double Top { get; init; }
        public double Bottom { get; init; }
    }
}
