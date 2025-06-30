using UglyToad.PdfPig;

namespace MarketAssistant.Vectors;

/// <summary>
/// 用于读取PDF文档并提取文本内容的工具类
/// </summary>
public class PdfReader
{
    /// <summary>
    /// 从PDF文档中读取段落文本
    /// </summary>
    /// <param name="documentContents">PDF文档内容流</param>
    /// <param name="documentUri">文档URI标识符</param>
    /// <returns>文本段落集合</returns>
    public static IEnumerable<TextParagraph> ReadParagraphs(Stream documentContents, string documentUri)
    {
        // 保持流的位置，以便多次读取
        documentContents.Position = 0;

        // 打开PDF文档
        using var pdfDocument = PdfDocument.Open(documentContents);

        // 遍历每一页
        for (var i = 0; i < pdfDocument.NumberOfPages; i++)
        {
            // 获取当前页面
            var page = pdfDocument.GetPage(i + 1);

            // 提取页面文本并按段落分割
            var pageText = page.Text;

            // 如果页面文本为空则跳过
            if (string.IsNullOrEmpty(pageText))
            {
                continue;
            }

            // 根据空行分割文本为段落
            var paragraphs = SplitIntoParagraphs(pageText);

            // 遍历段落
            for (int j = 0; j < paragraphs.Length; j++)
            {
                var paragraphText = paragraphs[j].Trim();

                // 如果段落文本为空则跳过
                if (string.IsNullOrWhiteSpace(paragraphText))
                {
                    continue;
                }

                // 生成段落ID
                var paragraphId = $"page_{i + 1}_paragraph_{j + 1}";

                Console.WriteLine("Found paragraph:");
                Console.WriteLine(paragraphText);
                Console.WriteLine();

                // 返回文本段落对象
                yield return new TextParagraph
                {
                    Key = Guid.NewGuid().ToString(),
                    DocumentUri = documentUri,
                    ParagraphId = paragraphId,
                    Text = paragraphText
                };
            }
        }
    }

    /// <summary>
    /// 将文本按段落分割
    /// </summary>
    /// <param name="text">要分割的文本</param>
    /// <returns>段落数组</returns>
    private static string[] SplitIntoParagraphs(string text)
    {
        // 处理不同类型的换行符
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");

        // 使用连续两个或更多换行符作为段落分隔符
        var paragraphs = text.Split(new[] { "\n\n" }, StringSplitOptions.None);

        // 处理单行文本，可能需要进一步分析
        var result = new List<string>();
        foreach (var paragraph in paragraphs)
        {
            // 如果段落包含单个换行符，可能是PDF布局导致的不正确换行
            // 这里用空格替换单个换行符，保持段落完整性
            var cleanedParagraph = paragraph.Replace("\n", " ").Trim();

            if (!string.IsNullOrWhiteSpace(cleanedParagraph))
            {
                result.Add(cleanedParagraph);
            }
        }

        return result.ToArray();
    }
}
