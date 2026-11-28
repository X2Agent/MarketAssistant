using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.ViewModels;
using MarketAssistant.Views.Components;
using SkiaSharp;
using AdaptiveCardConverter = MarketAssistant.Infrastructure.AdaptiveCards.AdaptiveCardConverter;

namespace MarketAssistant.Services.Export;

/// <summary>
/// 报告长图渲染器：把整份分析报告离屏渲染为 PNG 字节或 PDF 文件。
/// 内容是页面同一个 <see cref="AnalysisReportView"/> 加上页面没有的附录（关键指标、各分析师详细分析），
/// 导出即所见，不引入第二套排版；先按窄于桌面窗口的排版宽度（<see cref="LayoutWidth"/> DIP）排版，
/// 再整体放大到 <see cref="OutputWidth"/> 像素，正文在手机上也读得清；高度随内容。
/// PDF 由同一张位图按 A4 页高切段（<see cref="ReportPdfPager"/>）后经 SkiaSharp 封装，与分享图视觉一致。
/// 必须在 UI 线程调用（RenderTargetBitmap 与 Window 均要求）。
/// </summary>
public static class ReportImageRenderer
{
    /// <summary>输出位图宽度（像素）。</summary>
    private const int OutputWidth = 1080;

    /// <summary>视图排版宽度（DIP）。</summary>
    private const double LayoutWidth = 720;

    /// <summary>位图相对排版尺寸的放大倍率。</summary>
    private const double Scale = OutputWidth / LayoutWidth;

    /// <summary>A4 页宽（pt，1pt = 1/72 英寸）。</summary>
    private const float PdfPageWidth = 595.28f;

    /// <summary>A4 页高（pt）。</summary>
    private const float PdfPageHeight = 841.89f;

    /// <summary>PDF 页面上下留白（pt）；左右不留，长图自带与页面一致的内边距。</summary>
    private const float PdfPageMargin = 20f;

    /// <summary>
    /// 显式指定 PNG 编码参数以走非过时重载：<c>Save(Stream, int?)</c> 缺少编码器类型，
    /// 在无扩展名的 <see cref="MemoryStream"/> 上格式判定不够显式。
    /// </summary>
    private static readonly BitmapEncoderOptions PngEncoderOptions = new PngBitmapEncoderOptions();

    /// <summary>渲染报告长图为 PNG 字节。</summary>
    public static byte[] RenderToPngBytes(MarketAnalysisReport report, string assetName, AdaptiveCardConverter converter)
    {
        using var bitmap = Render(report, assetName, converter);
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngEncoderOptions);
        return stream.ToArray();
    }

    /// <summary>
    /// 渲染报告长图并按 A4 分页封装为 PDF 文件：位图缩放到页宽，按页高切段，页面上下留白、底色与长图一致。
    /// </summary>
    public static void SavePdf(MarketAnalysisReport report, string assetName, AdaptiveCardConverter converter, string filePath)
    {
        using var bitmap = Render(report, assetName, converter);

        using var pngStream = new MemoryStream();
        bitmap.Save(pngStream, PngEncoderOptions);
        pngStream.Position = 0;

        using var source = SKBitmap.Decode(pngStream);
        using var image = SKImage.FromBitmap(source);

        var pointsPerPixel = PdfPageWidth / source.Width;
        var pageRows = (int)((PdfPageHeight - 2 * PdfPageMargin) / pointsPerPixel);
        var pages = ReportPdfPager.PlanPages(source.GetPixelSpan(), source.RowBytes, source.Height, pageRows);

        // 页面底色取长图左上角（页面背景色），上下留白才与内容连成一片
        var pageColor = source.GetPixel(0, 0);
        var sampling = new SKSamplingOptions(SKFilterMode.Linear);

        using var document = SKDocument.CreatePdf(filePath);
        foreach (var (top, height) in pages)
        {
            using var canvas = document.BeginPage(PdfPageWidth, PdfPageHeight);
            canvas.Clear(pageColor);
            canvas.DrawImage(
                image,
                new SKRect(0, top, source.Width, top + height),
                new SKRect(0, PdfPageMargin, PdfPageWidth, PdfPageMargin + height * pointsPerPixel),
                sampling,
                null);
            document.EndPage();
        }
    }

    /// <summary>
    /// 离屏渲染报告长图。展示数据由 <paramref name="report"/> 现建（<see cref="ReportImageViewModel.Create"/>），
    /// 与页面当前绑定的实例解耦，导出内容只取决于传入的报告。
    /// 游离的控件（未挂入任何窗口树）不会套用应用主题与控件模板，视觉树里只剩空壳，
    /// 渲染结果是整张透明图；因此借助一个从未显示的 <see cref="Window"/> 完成样式与模板，渲染完立即关闭。
    /// 宿主窗口的尺寸与导出无关，内容高度由视图按固定宽度、不限高度单独测得。
    /// </summary>
    private static RenderTargetBitmap Render(MarketAnalysisReport report, string assetName, AdaptiveCardConverter converter)
    {
        var view = new ReportImageView
        {
            DataContext = ReportImageViewModel.Create(report, assetName, converter),
            Width = LayoutWidth
        };
        var host = new Window { Content = view };
        try
        {
            host.Measure(new Size(LayoutWidth, double.PositiveInfinity));

            // 排两遍：ProgressBar 在 Arrange 里才改写指示块宽度，指示块随即失效；
            // 手动布局没有布局管理器的后续轮次，不把整棵树重新失效再排一遍，进度条填充就画不出来
            var pixelHeight = 0;
            for (var pass = 0; pass < 2; pass++)
            {
                foreach (var layoutable in view.GetVisualDescendants().OfType<Layoutable>().Prepend(view))
                {
                    layoutable.InvalidateMeasure();
                }

                view.Measure(new Size(LayoutWidth, double.PositiveInfinity));
                // 高度对齐到整像素：否则位图最后一行只被背景盖住一部分，会残留半透明的一行
                pixelHeight = (int)Math.Ceiling(view.DesiredSize.Height * Scale);
                view.Arrange(new Rect(0, 0, LayoutWidth, pixelHeight / Scale));
            }

            var bitmap = new RenderTargetBitmap(new PixelSize(OutputWidth, pixelHeight), new Vector(96 * Scale, 96 * Scale));
            bitmap.Render(view);
            return bitmap;
        }
        finally
        {
            host.Close();
        }
    }
}
