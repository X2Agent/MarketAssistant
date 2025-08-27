using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MarketAssistant.Vectors.Services;

namespace TestMarketAssistant.Vectors;

[TestClass]
public class DocxBlockReaderTest
{
    [TestMethod]
    public void ReadBlocks_ShouldReturnTextBlocks()
    {
        // create a simple docx in memory with two paragraphs
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, DocumentFormat.OpenXml.WordprocessingDocumentType.Document, true))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(new Paragraph(new Run(new Text("Hello world"))), new Paragraph(new Run(new Text("Second paragraph")))));
            main.Document.Save();
        }
        ms.Position = 0;

        var reader = new DocxBlockReader();
        var blocks = reader.ReadBlocks(ms, "test.docx").ToList();

        Assert.IsTrue(blocks.Any(b => b.Type == DocumentBlockType.Text && b.Text?.Contains("Hello world") == true));
        Assert.IsTrue(blocks.Any(b => b.Type == DocumentBlockType.Text && b.Text?.Contains("Second paragraph") == true));
    }
}
