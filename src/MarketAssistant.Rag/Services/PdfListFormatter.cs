namespace MarketAssistant.Rag.Services;

/// <summary>
/// PDF 列表识别、缩进计算与段落合并判断（internal 静态类，纯算法，无状态）。
/// </summary>
internal static class PdfListFormatter
{
    internal static bool IsNumberedList(string text) => PdfTextUtility.NumberedListRegex.IsMatch(text);

    internal static bool IsBulletList(string text) => PdfTextUtility.BulletListRegex.IsMatch(text);

    internal static int CalculateIndentLevel(double leftMargin, List<StructuredLine> allLines)
    {
        if (!allLines.Any()) return 0;

        var avgMargin = allLines.Average(l => l.LeftMargin);
        var marginDiff = leftMargin - avgMargin;

        return Math.Max(0, (int)(marginDiff / 20)); // 每20个单位为一个缩进级别
    }

    internal static bool ShouldMergeWithPrevious(StructuredLine current, List<StructuredLine> lines, int currentIndex)
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
}
