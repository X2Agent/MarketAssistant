namespace MarketAssistant.Rag.Services;

/// <summary>
/// PDF 标题识别与层级判定（internal 静态类，纯算法，无状态）。
/// </summary>
internal static class PdfHeadingDetector
{
    internal static bool IsHeading(StructuredLine line, DocumentInfo docInfo)
    {
        var text = line.Text.Trim();
        if (string.IsNullOrEmpty(text)) return false;

        // 检查章节编号
        if (PdfTextUtility.ChapterNumberRegex.IsMatch(text) || PdfTextUtility.SectionNumberRegex.IsMatch(text) ||
            PdfTextUtility.ChineseSectionRegex.IsMatch(text) || PdfTextUtility.ChineseSubSectionRegex.IsMatch(text))
            return true;

        // 检查字体大小
        var fontSizeRatio = line.FontSize / docInfo.AverageFontSize;
        if (fontSizeRatio >= docInfo.DynamicThresholds.Level3Threshold)
        {
            // 附加条件：较短的文本，不以句号结尾，首字符是字母、数字或中文字符
            return text.Length <= 100 &&
                   !text.EndsWith('.') &&
                   (char.IsUpper(text[0]) || char.IsLetter(text[0]) || char.IsDigit(text[0]) || PdfTextUtility.IsChinese(text[0])) &&
                   text.Split(' ').Length <= 10;
        }

        // 检查粗体
        if (line.IsBold && text.Length <= 100 && !text.EndsWith('.'))
            return true;

        return false;
    }

    internal static int DetermineHeadingLevel(StructuredLine line, DocumentInfo docInfo)
    {
        var text = line.Text.Trim();
        var fontSizeRatio = line.FontSize / docInfo.AverageFontSize;
        var thresholds = docInfo.DynamicThresholds;

        // 1. 最高优先级：章节编号 (第X章)
        if (PdfTextUtility.ChapterNumberRegex.IsMatch(text))
            return 1;

        // 2. 高优先级：明确的编号格式
        // 中文主要章节编号 (一、二、三、...)
        if (PdfTextUtility.ChineseSectionRegex.IsMatch(text))
        {
            // 如果字体很大，可能是一级标题，否则是二级
            return fontSizeRatio >= thresholds.Level1Threshold ? 1 : 2;
        }

        // 中文子章节编号 (（一）、（二）、...)
        if (PdfTextUtility.ChineseSubSectionRegex.IsMatch(text))
            return 3;

        // 阿拉伯数字编号 (1.1, 1.2, ...)
        if (PdfTextUtility.SectionNumberRegex.IsMatch(text))
        {
            var match = PdfTextUtility.SectionNumberRegex.Match(text);
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

    internal static DynamicThresholds CalculateDynamicThresholds(List<HeadingCandidate> candidates, double avgFontSize)
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
                Level2Threshold = Math.Max(ratio - 0.2, PdfTextUtility.SmallHeadingThreshold),
                Level3Threshold = PdfTextUtility.SmallHeadingThreshold,
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
                Level3Threshold = Math.Max(fontRatios[1] - 0.1, PdfTextUtility.SmallHeadingThreshold),
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
}
