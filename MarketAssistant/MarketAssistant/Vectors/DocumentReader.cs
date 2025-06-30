using DocumentFormat.OpenXml.Packaging;
using System.Text;
using System.Xml;

namespace MarketAssistant.Vectors;

public class DocumentReader
{
    public static IEnumerable<TextParagraph> ReadParagraphs(Stream documentContents, string documentUri)
    {
        // Open the document.
        using WordprocessingDocument wordDoc = WordprocessingDocument.Open(documentContents, false);
        if (wordDoc.MainDocumentPart == null)
        {
            yield break;
        }

        // Create an XmlDocument to hold the document contents and load the document contents into the XmlDocument.
        XmlDocument xmlDoc = new XmlDocument();
        XmlNamespaceManager nsManager = new XmlNamespaceManager(xmlDoc.NameTable);
        nsManager.AddNamespace("w", "http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        nsManager.AddNamespace("w14", "http://schemas.microsoft.com/office/word/2010/wordml");

        xmlDoc.Load(wordDoc.MainDocumentPart.GetStream());

        // Select all paragraphs in the document and break if none found.
        XmlNodeList? paragraphs = xmlDoc.SelectNodes("//w:p", nsManager);
        if (paragraphs == null)
        {
            yield break;
        }

        // Iterate over each paragraph.
        foreach (XmlNode paragraph in paragraphs)
        {
            // Select all text nodes in the paragraph and continue if none found.
            XmlNodeList? texts = paragraph.SelectNodes(".//w:t", nsManager);
            if (texts == null)
            {
                continue;
            }

            // Combine all non-empty text nodes into a single string.
            var textBuilder = new StringBuilder();
            foreach (XmlNode text in texts)
            {
                if (!string.IsNullOrWhiteSpace(text.InnerText))
                {
                    textBuilder.Append(text.InnerText);
                }
            }

            // Yield a new TextParagraph if the combined text is not empty.
            var combinedText = textBuilder.ToString();
            if (!string.IsNullOrWhiteSpace(combinedText))
            {
                Console.WriteLine("Found paragraph:");
                Console.WriteLine(combinedText);
                Console.WriteLine();

                yield return new TextParagraph
                {
                    Key = Guid.NewGuid().ToString(),
                    DocumentUri = documentUri,
                    ParagraphId = paragraph.Attributes?["w14:paraId"]?.Value ?? string.Empty,
                    Text = combinedText
                };
            }
        }
    }
}
