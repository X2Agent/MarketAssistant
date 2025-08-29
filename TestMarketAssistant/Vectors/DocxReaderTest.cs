using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MarketAssistant.Vectors.Services;
using MarketAssistant.Vectors.Interfaces;

namespace TestMarketAssistant.Vectors;

[TestClass]
public class DocxReaderTest
{
    private DocxReader _reader = null!;
    private static readonly string TestDocxPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "demo.docx");

    [TestInitialize]
    public void Setup()
    {
        _reader = new DocxReader();
    }

    [TestMethod]
    public void CanRead_DocxFile_ReturnsTrue()
    {
        // Arrange & Act
        var result = _reader.CanRead("test.docx");

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void CanRead_NonDocxFile_ReturnsFalse()
    {
        // Arrange & Act
        var txtResult = _reader.CanRead("test.txt");
        var pdfResult = _reader.CanRead("test.pdf");

        // Assert
        Assert.IsFalse(txtResult);
        Assert.IsFalse(pdfResult);
    }

    [TestMethod]
    public void ReadBlocks_RealDocxFile_ReturnsBlocks()
    {
        // Arrange
        if (!File.Exists(TestDocxPath))
        {
            Assert.Inconclusive($"测试文件不存在: {TestDocxPath}");
            return;
        }

        // Act
        using var stream = File.OpenRead(TestDocxPath);
        var blocks = _reader.ReadBlocks(stream, TestDocxPath).ToList();

        // Assert
        Assert.IsNotNull(blocks);
        Assert.IsTrue(blocks.Count > 0, "应该至少读取到一些内容块");

        // 验证是否有文本块
        var textBlocks = blocks.Where(b => b.Type == DocumentBlockType.Text).ToList();
        Assert.IsTrue(textBlocks.Count > 0, "应该至少有一个文本块");

        // 验证文本块内容不为空
        Assert.IsTrue(textBlocks.Any(b => !string.IsNullOrWhiteSpace(b.Text)), "文本块应该包含实际内容");

        // 检查其他类型的块
        var imageBlocks = blocks.Where(b => b.Type == DocumentBlockType.Image).ToList();
        var tableBlocks = blocks.Where(b => b.Type == DocumentBlockType.Table).ToList();

        Console.WriteLine($"读取到 {blocks.Count} 个内容块：{textBlocks.Count} 个文本块，{imageBlocks.Count} 个图片块，{tableBlocks.Count} 个表格块");
    }

    [TestMethod]
    public void ReadAllText_RealDocxFile_ReturnsText()
    {
        // Arrange
        if (!File.Exists(TestDocxPath))
        {
            Assert.Inconclusive($"测试文件不存在: {TestDocxPath}");
            return;
        }

        // Act
        using var stream = File.OpenRead(TestDocxPath);
        var text = _reader.ReadAllText(stream);

        // Assert
        Assert.IsNotNull(text);
        Assert.IsTrue(text.Length > 0, "应该读取到文本内容");
        Assert.IsTrue(text.Trim().Length > 0, "文本内容不应该只是空白字符");

        Console.WriteLine($"读取到的文本长度: {text.Length} 字符");
        Console.WriteLine($"文本预览: {text.Substring(0, Math.Min(100, text.Length))}...");
    }

    [TestMethod]
    public void ReadBlocks_InMemoryDocx_ReturnsExpectedBlocks()
    {
        // Arrange - 创建一个简单的内存中 DOCX 文档
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, DocumentFormat.OpenXml.WordprocessingDocumentType.Document, true))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(
                new Paragraph(new Run(new Text("Hello world"))),
                new Paragraph(new Run(new Text("Second paragraph")))
            ));
            main.Document.Save();
        }
        ms.Position = 0;

        // Act
        var blocks = _reader.ReadBlocks(ms, "test.docx").ToList();

        // Assert
        Assert.IsNotNull(blocks);
        Assert.IsTrue(blocks.Any(b => b.Type == DocumentBlockType.Text && b.Text?.Contains("Hello world") == true));
        Assert.IsTrue(blocks.Any(b => b.Type == DocumentBlockType.Text && b.Text?.Contains("Second paragraph") == true));
    }
}
