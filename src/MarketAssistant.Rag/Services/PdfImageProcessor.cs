using System.Text;
using MarketAssistant.Rag.Interfaces;
using PdfPage = UglyToad.PdfPig.Content.Page;

namespace MarketAssistant.Rag.Services;

/// <summary>
/// PDF 页内图片提取与 Markdown 图片引用生成（依赖 <see cref="IImageStorageService"/> 持久化）。
/// </summary>
internal sealed class PdfImageProcessor
{
    private readonly IImageStorageService _imageStorageService;

    public PdfImageProcessor(IImageStorageService imageStorageService)
    {
        _imageStorageService = imageStorageService ?? throw new ArgumentNullException(nameof(imageStorageService));
    }

    public async Task ProcessPageImages(PdfPage page, StringBuilder markdown, int pageNumber, string filePath)
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

    private static byte[]? ExtractImageBytes(UglyToad.PdfPig.Content.IPdfImage image)
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
}
