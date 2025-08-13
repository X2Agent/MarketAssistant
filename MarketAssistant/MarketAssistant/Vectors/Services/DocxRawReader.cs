using System.Text;
using System.Xml;
using DocumentFormat.OpenXml.Packaging;
using MarketAssistant.Vectors.Interfaces;

namespace MarketAssistant.Vectors.Services;

/// <summary>
/// DOCX 原始文本读取器
/// </summary>
public class DocxRawReader : IRawDocumentReader
{
    public bool CanRead(string filePath) => filePath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);

    public string ReadAllText(Stream stream)
    {
        stream.Position = 0;
        using var wordDoc = WordprocessingDocument.Open(stream, false);
        if (wordDoc.MainDocumentPart == null) return string.Empty;

        var xmlDoc = new XmlDocument();
        var ns = new XmlNamespaceManager(xmlDoc.NameTable);
        ns.AddNamespace("w", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");

        xmlDoc.Load(wordDoc.MainDocumentPart.GetStream());
        var paragraphs = xmlDoc.SelectNodes("//w:p", ns);
        if (paragraphs == null || paragraphs.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        foreach (XmlNode p in paragraphs)
        {
            var texts = p.SelectNodes(".//w:t", ns);
            if (texts == null) continue;
            var line = new StringBuilder();
            foreach (XmlNode t in texts)
            {
                if (!string.IsNullOrWhiteSpace(t.InnerText)) line.Append(t.InnerText);
            }
            var s = line.ToString().Trim();
            if (s.Length > 0) sb.AppendLine(s);
        }
        return sb.ToString();
    }
}


