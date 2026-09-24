using Avalonia;
using Avalonia.Media.Imaging;
using MarketAssistant.ViewModels;
using MarketAssistant.Views.Components;
using SkiaSharp;

namespace MarketAssistant.Services.Export;

/// <summary>
/// 报告分享卡渲染器：把 <see cref="ReportShareCardData"/> 离屏渲染为图片或 PDF。
/// 渲染走 Avalonia 视觉树（中文与应用字体由宿主保证），PDF 由渲染位图经 SkiaSharp 封装，
/// 与分享图视觉一致，不引入第二套排版实现。
/// 必须在 UI 线程调用（RenderTargetBitmap 要求）。
/// </summary>
public static class ReportShareCardRenderer
{
    public const int CardWidth = 1080;

    public const int CardHeight = 1350;

    private static readonly PixelSize CardPixelSize = new(CardWidth, CardHeight);

    /// <summary>渲染分享卡为 PNG 字节。</summary>
    public static byte[] RenderToPngBytes(ReportShareCardData data)
    {
        var view = CreateCard(data);
        using var bitmap = Render(view);
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }

    /// <summary>渲染分享卡并保存为 PNG 文件。</summary>
    public static void SavePng(ReportShareCardData data, string filePath)
    {
        var bytes = RenderToPngBytes(data);
        File.WriteAllBytes(filePath, bytes);
    }

    /// <summary>
    /// 渲染分享卡并封装为 PDF 文件（单页，页面尺寸即卡片像素尺寸）。
    /// </summary>
    public static void SavePdf(ReportShareCardData data, string filePath)
    {
        var view = CreateCard(data);
        using var bitmap = Render(view);

        using var pngStream = new MemoryStream();
        bitmap.Save(pngStream);
        pngStream.Position = 0;

        using var input = SKBitmap.Decode(pngStream);
        using var document = SKDocument.CreatePdf(filePath);
        using var canvas = document.BeginPage(CardWidth, CardHeight);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(input, new SKRect(0, 0, CardWidth, CardHeight));
        document.EndPage();
    }

    private static ReportShareCardView CreateCard(ReportShareCardData data)
        => new(data) { Width = CardWidth, Height = CardHeight };

    private static RenderTargetBitmap Render(ReportShareCardView view)
    {
        view.Measure(new Size(CardWidth, CardHeight));
        view.Arrange(new Rect(0, 0, CardWidth, CardHeight));

        var bitmap = new RenderTargetBitmap(CardPixelSize, new Vector(96, 96));
        bitmap.Render(view);
        return bitmap;
    }
}
