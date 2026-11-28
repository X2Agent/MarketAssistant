namespace MarketAssistant.Services.Export;

/// <summary>
/// PDF 分页规划：把一张长位图切成页高固定的若干段，切口优先落在卡片之间的空隙，其次落在卡片内没有文字的空白行，
/// 避免一行字被劈成两半。只做像素计算，不依赖渲染平台，因此可以脱离 Avalonia / SkiaSharp 单元测试。
/// </summary>
public static class ReportPdfPager
{
    /// <summary>每个像素的字节数（BGRA / RGBA 均为 4）。</summary>
    private const int BytesPerPixel = 4;

    /// <summary>
    /// 相邻像素三通道差之和超过该值才算一次颜色跳变。必须压得很小：浅色主题下白色卡片与页面底色总共只差 22，
    /// 取大了卡片边缘就探测不到，卡片内部会被误当成卡片之间的空隙。平涂色块之间没有渐变，只需容忍舍入误差。
    /// </summary>
    private const int ColorTolerance = 6;

    /// <summary>整行同色（没有任何跳变）：卡片之间的空隙是整行页面底色，在这里切开每张卡片都保持完整。</summary>
    private const int UniformRowTransitions = 0;

    /// <summary>
    /// 卡片内空白行允许的颜色跳变数：卡片边缘、卡片内浅色底块的边缘每处只算一两次，
    /// 文字行（哪怕只有一个词）的跳变数远超于此。
    /// </summary>
    private const int MaxBlankTransitions = 12;

    /// <summary>
    /// 卡片之间的切口前面那块内容至少要有这么高（位图行）：比它矮的是章节标题，不能独自留在页尾，要跟着下一张卡片去下一页。
    /// 最矮的卡片也有上下内边距加一行字，远高于此。
    /// </summary>
    private const int MinSeparatedBlockHeight = 80;

    /// <summary>每页至少填满的比例：切口只在页面下半段找，避免为了凑空隙把页面切得过短。</summary>
    private const double MinPageFill = 0.5;

    /// <summary>
    /// 规划分页，返回每页的（起始行，行数），各页首尾相接覆盖整张位图。
    /// 每页最多 <paramref name="pageHeight"/> 行：在页面下半段从下往上找最后一段切口，切在这段空白的正中。
    /// 先找卡片之间的整行空隙；整页都没有（卡片比一页还高）才退到卡片内的空白行；再没有（例如超高的纯色块）才在页高处硬切。
    /// </summary>
    /// <param name="pixels">整张位图的像素，每像素 4 字节，行与行之间按 <paramref name="rowBytes"/> 排列。</param>
    /// <param name="rowBytes">位图一行的字节数。</param>
    /// <param name="totalHeight">位图总行数。</param>
    /// <param name="pageHeight">每页可容纳的位图行数。</param>
    public static IReadOnlyList<(int Top, int Height)> PlanPages(
        ReadOnlySpan<byte> pixels,
        int rowBytes,
        int totalHeight,
        int pageHeight)
    {
        var pages = new List<(int Top, int Height)>();
        var top = 0;
        while (totalHeight - top > pageHeight)
        {
            var minCut = top + (int)(pageHeight * MinPageFill);
            var maxCut = top + pageHeight;
            var cut = FindCut(pixels, rowBytes, top, minCut, maxCut, UniformRowTransitions, MinSeparatedBlockHeight)
                      ?? FindCut(pixels, rowBytes, top, minCut, maxCut, MaxBlankTransitions, minBlockHeight: 0)
                      ?? maxCut;
            pages.Add((top, cut - top));
            top = cut;
        }

        pages.Add((top, totalHeight - top));
        return pages;
    }

    /// <summary>
    /// 在 [<paramref name="minCut"/>, <paramref name="maxCut"/>] 内找切口（切口行属于下一页）：
    /// 从下往上取第一段“前面的内容块不低于 <paramref name="minBlockHeight"/>”的空白行，切在这段空白的正中；没有则返回 null。
    /// </summary>
    private static int? FindCut(
        ReadOnlySpan<byte> pixels,
        int rowBytes,
        int top,
        int minCut,
        int maxCut,
        int maxTransitions,
        int minBlockHeight)
    {
        var searchFrom = maxCut - 1;
        while (searchFrom >= minCut)
        {
            var bandEnd = searchFrom;
            while (bandEnd >= minCut && !IsBlankRow(RowAt(pixels, rowBytes, bandEnd), maxTransitions))
            {
                bandEnd--;
            }

            if (bandEnd < minCut)
            {
                return null;
            }

            var bandStart = bandEnd;
            while (bandStart > top && IsBlankRow(RowAt(pixels, rowBytes, bandStart - 1), maxTransitions))
            {
                bandStart--;
            }

            var blockTop = bandStart;
            while (blockTop > top && !IsBlankRow(RowAt(pixels, rowBytes, blockTop - 1), maxTransitions))
            {
                blockTop--;
            }

            if (bandStart - blockTop >= minBlockHeight)
            {
                return (Math.Max(bandStart, minCut) + bandEnd) / 2 + 1;
            }

            searchFrom = blockTop - 1;
        }

        return null;
    }

    private static ReadOnlySpan<byte> RowAt(ReadOnlySpan<byte> pixels, int rowBytes, int row)
        => pixels.Slice(row * rowBytes, rowBytes);

    private static bool IsBlankRow(ReadOnlySpan<byte> row, int maxTransitions)
    {
        var transitions = 0;
        for (var offset = BytesPerPixel; offset + 2 < row.Length; offset += BytesPerPixel)
        {
            var difference = Math.Abs(row[offset] - row[offset - BytesPerPixel])
                           + Math.Abs(row[offset + 1] - row[offset - BytesPerPixel + 1])
                           + Math.Abs(row[offset + 2] - row[offset - BytesPerPixel + 2]);
            if (difference > ColorTolerance && ++transitions > maxTransitions)
            {
                return false;
            }
        }

        return true;
    }
}
