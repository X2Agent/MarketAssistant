using System.Text;
using MarketAssistant.Vectors.Interfaces;
using UglyToad.PdfPig;

namespace MarketAssistant.Vectors.Services;

/// <summary>
/// PDF 原始文本读取器
/// </summary>
public class PdfRawReader : IRawDocumentReader
{
    public bool CanRead(string filePath) => filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    public string ReadAllText(Stream stream)
    {
        stream.Position = 0;
        using var pdf = PdfDocument.Open(stream);
        var sb = new StringBuilder();
        for (int i = 1; i <= pdf.NumberOfPages; i++)
        {
            var page = pdf.GetPage(i);
            var text = page.Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                // 将 PDF 页内的单行换行转为空格，页与页之间保留空行
                text = text.Replace("\r\n", "\n").Replace("\r", "\n");
                var lines = text.Split('\n');
                var line = string.Join(' ', lines.Select(s => s.Trim()).Where(s => s.Length > 0));
                if (line.Length > 0)
                {
                    sb.AppendLine(line);
                    sb.AppendLine();
                }
            }
        }
        return sb.ToString();
    }
}


