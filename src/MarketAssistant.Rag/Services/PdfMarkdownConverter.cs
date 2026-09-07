using MarketAssistant.Rag.Interfaces;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using PdfPage = UglyToad.PdfPig.Content.Page;

namespace MarketAssistant.Rag.Services;

/// <summary>
/// PDF到Markdown转换器
/// 使用UglyToad.PdfPig提取文本并转换为Markdown格式
/// 支持标题识别、段落处理、列表识别、表格处理和图片引用。
/// 表格/标题/列表/图片/后处理等算法已下沉到各自辅助类（PdfTableExtractor 等），本类保留编排与文档分析。
/// </summary>
public class PdfMarkdownConverter : IMarkdownConverter
{
    private readonly PdfImageProcessor _pdfImageProcessor;

    public PdfMarkdownConverter(IImageStorageService imageStorageService)
    {
        ArgumentNullException.ThrowIfNull(imageStorageService);
        _pdfImageProcessor = new PdfImageProcessor(imageStorageService);
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

        return PdfMarkdownPostProcessor.PostProcessMarkdown(markdownBuilder.ToString());
    }

    private DocumentInfo AnalyzeDocument(PdfDocument document)
    {
        var allFontSizes = new List<double>();
        var headingCandidates = new List<HeadingCandidate>();
        var wordCount = 0;
        double fontSum = 0;

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
                        fontSum += fontSize;
                        wordCount++;
                    }
                }

                // 收集潜在的标题候选（当前平均值由增量累加得出，避免逐行重算 Average）
                foreach (var line in lines)
                {
                    var text = line.Text.Trim();
                    if (!string.IsNullOrEmpty(text) && text.Length <= 100 && !text.EndsWith('.'))
                    {
                        var currentAvgFontSize = wordCount > 0 ? fontSum / wordCount : 12.0;
                        var fontSizeRatio = line.FontSize / currentAvgFontSize;
                        if (fontSizeRatio >= 1.1 || line.IsBold ||
                            PdfTextUtility.ChapterNumberRegex.IsMatch(text) ||
                            PdfTextUtility.ChineseSectionRegex.IsMatch(text) ||
                            PdfTextUtility.ChineseSubSectionRegex.IsMatch(text) ||
                            PdfTextUtility.SectionNumberRegex.IsMatch(text))
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
        var headingThresholds = PdfHeadingDetector.CalculateDynamicThresholds(headingCandidates, avgFontSize);

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
            var tables = PdfTableExtractor.DetectTables(structuredLines);

            // 处理结构化内容
            ProcessStructuredContent(structuredLines, tables, markdown, docInfo);

            // 处理图片
            await _pdfImageProcessor.ProcessPageImages(page, markdown, pageNumber, filePath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"处理页面内容时出错: {ex.Message}");
            // 降级到简单文本提取
            var fallbackText = page.Text;
            if (!string.IsNullOrWhiteSpace(fallbackText))
            {
                markdown.AppendLine(PdfTextUtility.CleanText(fallbackText));
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

    private void ProcessStructuredContent(List<StructuredLine> lines, List<TableInfo> tables, StringBuilder markdown, DocumentInfo docInfo)
    {
        var processedIndices = new HashSet<int>();

        // 改进的处理逻辑：按顺序处理所有行，确保表格和文本都正确处理
        for (int i = 0; i < lines.Count; i++)
        {
            if (processedIndices.Contains(i)) continue;

            var line = lines[i];
            var cleanText = PdfTextUtility.CleanText(line.Text);

            if (string.IsNullOrWhiteSpace(cleanText)) continue;

            // 检查是否是表格的开始
            var table = tables.FirstOrDefault(t => i == t.StartIndex);
            if (table != null)
            {
                PdfTableExtractor.ProcessTable(table, markdown);

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
            if (PdfHeadingDetector.IsHeading(line, docInfo))
            {
                var level = PdfHeadingDetector.DetermineHeadingLevel(line, docInfo);
                markdown.AppendLine($"{new string('#', level)} {cleanText}");
                markdown.AppendLine();
            }
            // 列表检测
            else if (PdfListFormatter.IsNumberedList(cleanText))
            {
                var match = PdfTextUtility.NumberedListRegex.Match(cleanText);
                var originalNumber = match.Groups[1].Value; // 提取原始编号（如 "1.", "2)", "(3)" 等）
                var listText = PdfTextUtility.NumberedListRegex.Replace(cleanText, "").Trim();
                var indent = PdfListFormatter.CalculateIndentLevel(line.LeftMargin, lines);

                // 规范化编号格式为标准的 "数字." 格式
                var numberOnly = Regex.Replace(originalNumber, @"[^\d]", "");
                markdown.AppendLine($"{new string(' ', indent * 2)}{numberOnly}. {listText}");
            }
            else if (PdfListFormatter.IsBulletList(cleanText))
            {
                var listText = PdfTextUtility.BulletListRegex.Replace(cleanText, "").Trim();
                var indent = PdfListFormatter.CalculateIndentLevel(line.LeftMargin, lines);
                markdown.AppendLine($"{new string(' ', indent * 2)}- {listText}");
            }
            // 普通段落
            else
            {
                // 检查是否应该与前一行合并
                if (PdfListFormatter.ShouldMergeWithPrevious(line, lines, i))
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
}
