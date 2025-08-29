using Microsoft.VisualStudio.TestTools.UnitTesting;
using MarketAssistant.Vectors.Services;
using MarketAssistant.Vectors.Interfaces;
using System.Text;

namespace TestMarketAssistant;

/// <summary>
/// PdfReader 单元测试
/// </summary>
[TestClass]
public class PdfReaderTest
{
    private PdfReader _reader = null!;
    private static readonly string TestPdfPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "demo.pdf");

    [TestInitialize]
    public void Setup()
    {
        _reader = new PdfReader();
    }

    [TestMethod]
    public void CanRead_PdfFile_ReturnsTrue()
    {
        // Arrange & Act
        var result = _reader.CanRead("test.pdf");
        
        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void CanRead_NonPdfFile_ReturnsFalse()
    {
        // Arrange & Act
        var txtResult = _reader.CanRead("test.txt");
        var docxResult = _reader.CanRead("test.docx");
        
        // Assert
        Assert.IsFalse(txtResult);
        Assert.IsFalse(docxResult);
    }

    [TestMethod]
    public void ReadBlocks_RealPdfFile_ReturnsBlocks()
    {
        // Arrange
        if (!File.Exists(TestPdfPath))
        {
            Assert.Inconclusive($"测试文件不存在: {TestPdfPath}");
            return;
        }

        // Act
        using var stream = File.OpenRead(TestPdfPath);
        var blocks = _reader.ReadBlocks(stream, TestPdfPath).ToList();

        // Assert
        Assert.IsNotNull(blocks);
        Assert.IsTrue(blocks.Count > 0, "应该至少读取到一些内容块");
        
        // 验证是否有文本块
        var textBlocks = blocks.Where(b => b.Type == DocumentBlockType.Text).ToList();
        Assert.IsTrue(textBlocks.Count > 0, "应该至少有一个文本块");
        
        // 验证文本块内容不为空
        Assert.IsTrue(textBlocks.Any(b => !string.IsNullOrWhiteSpace(b.Text)), "文本块应该包含实际内容");
        
        Console.WriteLine($"读取到 {blocks.Count} 个内容块，其中 {textBlocks.Count} 个文本块");
    }

    [TestMethod]
    public void ReadAllText_RealPdfFile_ReturnsText()
    {
        // Arrange
        if (!File.Exists(TestPdfPath))
        {
            Assert.Inconclusive($"测试文件不存在: {TestPdfPath}");
            return;
        }

        // Act
        using var stream = File.OpenRead(TestPdfPath);
        var text = _reader.ReadAllText(stream);

        // Assert
        Assert.IsNotNull(text);
        Assert.IsTrue(text.Length > 0, "应该读取到文本内容");
        Assert.IsTrue(text.Trim().Length > 0, "文本内容不应该只是空白字符");
        
        Console.WriteLine($"读取到的文本长度: {text.Length} 字符");
        Console.WriteLine($"文本预览: {text.Substring(0, Math.Min(100, text.Length))}...");
    }

    [TestMethod]
    public void ReadBlocks_NullStream_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.ThrowsException<ArgumentNullException>(() => 
        {
            _reader.ReadBlocks(null!, "test.pdf").ToList();
        });
    }
}
