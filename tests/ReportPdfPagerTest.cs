using MarketAssistant.Services.Export;

namespace TestMarketAssistant;

/// <summary>
/// PDF 分页规划只看像素：用合成位图（页面底色 / 白色卡片 / 文字行）验证切口落点，不依赖任何渲染平台。
/// 卡片与页面底色取浅色主题的真实色值——两者三通道总共只差 22，是容差取大后卡片边缘探测不到的那个回归点。
/// </summary>
[TestClass]
public sealed class ReportPdfPagerTest
{
    private const int Width = 40;
    private const int RowBytes = Width * 4;
    private const int CardInset = 4;

    private static readonly byte[] PageColor = [251, 247, 245, 255];
    private static readonly byte[] CardColor = [255, 255, 255, 255];
    private static readonly byte[] InkColor = [0, 0, 0, 255];

    private enum RowKind
    {
        /// <summary>整行页面底色：卡片之间的空隙。</summary>
        Gap,

        /// <summary>卡片内没有文字的行：只有卡片左右边缘两次跳变。</summary>
        CardBlank,

        /// <summary>卡片内的文字行：大量跳变。</summary>
        CardText,

        /// <summary>直接写在页面底色上的文字行（章节标题）。</summary>
        PageText
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void PlanPages_ContentShorterThanOnePage_ShouldReturnSinglePage()
    {
        var pixels = Bitmap((RowKind.CardText, 100));

        var pages = ReportPdfPager.PlanPages(pixels, RowBytes, 100, pageHeight: 150);

        CollectionAssert.AreEqual(new[] { (0, 100) }, pages.ToArray());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void PlanPages_CardsSeparatedByGaps_ShouldCutInTheMiddleOfTheLastGapThatFits()
    {
        // 三张 100 行的卡片，间隔 20 行：第一页容得下卡片 A，卡片 B 只露出一截，切口必须落在 A、B 之间的空隙正中
        var pixels = Bitmap(
            (RowKind.CardText, 100), (RowKind.Gap, 20),
            (RowKind.CardText, 100), (RowKind.Gap, 20),
            (RowKind.CardText, 100));

        var pages = ReportPdfPager.PlanPages(pixels, RowBytes, 340, pageHeight: 150);

        CollectionAssert.AreEqual(new[] { (0, 110), (110, 120), (230, 110) }, pages.ToArray());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void PlanPages_PagesShouldCoverTheBitmapWithoutOverlapOrOverflow()
    {
        var pixels = Bitmap(
            (RowKind.CardText, 90), (RowKind.Gap, 18), (RowKind.PageText, 30), (RowKind.Gap, 10),
            (RowKind.CardText, 130), (RowKind.CardBlank, 12), (RowKind.CardText, 130), (RowKind.Gap, 18),
            (RowKind.CardText, 200));
        var totalHeight = pixels.Length / RowBytes;

        var pages = ReportPdfPager.PlanPages(pixels, RowBytes, totalHeight, pageHeight: 170);

        Assert.AreEqual(0, pages[0].Top);
        for (var index = 1; index < pages.Count; index++)
        {
            Assert.AreEqual(pages[index - 1].Top + pages[index - 1].Height, pages[index].Top);
        }

        Assert.AreEqual(totalHeight, pages[^1].Top + pages[^1].Height);
        Assert.IsTrue(pages.All(page => page.Height is > 0 and <= 170), "每页行数必须在 (0, 页高] 内");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void PlanPages_ShortHeadingBeforeGap_ShouldMoveToNextPageWithItsCard()
    {
        // 卡片 A、章节标题（30 行）、卡片 B：标题后面的空隙刚好落在页尾，但标题不能孤零零留在上一页
        var pixels = Bitmap(
            (RowKind.CardText, 100), (RowKind.Gap, 20),
            (RowKind.PageText, 30), (RowKind.Gap, 10),
            (RowKind.CardText, 140));

        var pages = ReportPdfPager.PlanPages(pixels, RowBytes, 300, pageHeight: 170);

        Assert.AreEqual(0, pages[0].Top);
        Assert.IsTrue(pages[0].Height is >= 100 and <= 120, $"切口应落在卡片 A 与标题之间的空隙，实际页高 {pages[0].Height}");
        Assert.IsLessThanOrEqualTo(120, pages[1].Top, "标题应从第二页顶部开始");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void PlanPages_CardInteriorLooksLikeGap_ShouldStillPreferTheRealGap()
    {
        // 卡片 B 的文字之后是一段无文字的卡片内部，位于页尾附近；浅色主题下它与页面底色只差 22，
        // 容差取大会把这段内部当成卡片之间的空隙，把卡片 B 从中间切开
        var pixels = Bitmap(
            (RowKind.CardText, 170), (RowKind.Gap, 20),
            (RowKind.CardText, 90), (RowKind.CardBlank, 50),
            (RowKind.CardText, 100));

        var pages = ReportPdfPager.PlanPages(pixels, RowBytes, 430, pageHeight: 300);

        Assert.IsTrue(pages[0].Height is >= 170 and <= 190, $"切口应落在两张卡片之间的空隙，实际页高 {pages[0].Height}");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void PlanPages_NoGapOnPage_ShouldCutAtBlankRowsInsideTheCard()
    {
        // 一张超过一页高的卡片：文字块之间有 10 行卡片内空白，切口只能落在空白里，不能把文字行劈开
        var pixels = Bitmap(
            (RowKind.CardText, 40), (RowKind.CardBlank, 10),
            (RowKind.CardText, 40), (RowKind.CardBlank, 10),
            (RowKind.CardText, 40), (RowKind.CardBlank, 10),
            (RowKind.CardText, 40));

        var pages = ReportPdfPager.PlanPages(pixels, RowBytes, 190, pageHeight: 95);

        Assert.IsTrue(pages[0].Height is >= 90 and <= 100, $"切口应落在 [90, 100) 的卡片内空白里，实际页高 {pages[0].Height}");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void PlanPages_GapAboveMinimumFill_ShouldNotProduceAShortPage()
    {
        // 空隙在页面 20% 处：为了凑空隙把第一页切得只剩一小截不值得；整页没有别的切口时，在页高处硬切
        var pixels = Bitmap(
            (RowKind.CardText, 30), (RowKind.Gap, 20),
            (RowKind.CardText, 350));

        var pages = ReportPdfPager.PlanPages(pixels, RowBytes, 400, pageHeight: 200);

        Assert.AreEqual(200, pages[0].Height);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void PlanPages_NoBlankRowAnywhere_ShouldFallBackToHardCutAtPageHeight()
    {
        var pixels = Bitmap((RowKind.CardText, 450));

        var pages = ReportPdfPager.PlanPages(pixels, RowBytes, 450, pageHeight: 200);

        CollectionAssert.AreEqual(new[] { (0, 200), (200, 200), (400, 50) }, pages.ToArray());
    }

    private static byte[] Bitmap(params (RowKind Kind, int Count)[] segments)
    {
        var pixels = new List<byte>();
        foreach (var (kind, count) in segments)
        {
            var row = Row(kind);
            for (var index = 0; index < count; index++)
            {
                pixels.AddRange(row);
            }
        }

        return [.. pixels];
    }

    private static byte[] Row(RowKind kind)
    {
        var row = new byte[RowBytes];
        for (var x = 0; x < Width; x++)
        {
            var insideCard = x >= CardInset && x < Width - CardInset;
            var color = kind switch
            {
                RowKind.Gap => PageColor,
                RowKind.CardBlank => insideCard ? CardColor : PageColor,
                RowKind.CardText => insideCard ? (x % 2 == 0 ? InkColor : CardColor) : PageColor,
                _ => insideCard && x % 2 == 0 ? InkColor : PageColor
            };
            color.CopyTo(row, x * 4);
        }

        return row;
    }
}
