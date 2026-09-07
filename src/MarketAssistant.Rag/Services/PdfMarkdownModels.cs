namespace MarketAssistant.Rag.Services;

/// <summary>
/// PDF→Markdown 转换管线共享的数据结构（internal，仅供本程序集使用）。
/// </summary>

internal sealed class DocumentInfo
{
    public double AverageFontSize { get; init; }
    public double MaxFontSize { get; init; }
    public double MinFontSize { get; init; }
    public int TotalWords { get; init; }
    public List<HeadingCandidate> HeadingCandidates { get; init; } = new();
    public DynamicThresholds DynamicThresholds { get; init; } = new();
}

internal sealed class HeadingCandidate
{
    public required string Text { get; init; }
    public double FontSize { get; init; }
    public bool IsBold { get; init; }
    public int PageNumber { get; init; }
}

internal sealed class DynamicThresholds
{
    public double Level1Threshold { get; init; } = PdfTextUtility.LargeHeadingThreshold;
    public double Level2Threshold { get; init; } = PdfTextUtility.MediumHeadingThreshold;
    public double Level3Threshold { get; init; } = PdfTextUtility.SmallHeadingThreshold;
    public List<double> FontSizeBreakpoints { get; init; } = new();
}

internal sealed class StructuredLine
{
    public required string Text { get; init; }
    public double FontSize { get; init; }
    public bool IsBold { get; init; }
    public bool IsItalic { get; init; }
    public double LeftMargin { get; init; }
    public double TopPosition { get; init; }
    public BoundingBox BoundingBox { get; init; } = new();
}

internal sealed class TableInfo
{
    public required List<StructuredLine> Rows { get; init; }
    public int StartIndex { get; init; }
    public int EndIndex { get; init; }
    public List<int> ActualRowIndices { get; init; } = new List<int>(); // 实际的表格行索引
}

internal sealed class BoundingBox
{
    public double Left { get; init; }
    public double Right { get; init; }
    public double Top { get; init; }
    public double Bottom { get; init; }
}
