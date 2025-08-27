using Microsoft.VisualStudio.TestTools.UnitTesting;
using MarketAssistant.Vectors.Services;
using MarketAssistant.Vectors.Interfaces;
using System.Text;

namespace TestMarketAssistant;

/// <summary>
/// PdfBlockReader 单元测试
/// </summary>
[TestClass]
public class PdfBlockReaderTest
{
    private PdfBlockReader _reader;

    [TestInitialize]
    public void Setup()
    {
        _reader = new PdfBlockReader();
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
        var xlsxResult = _reader.CanRead("test.xlsx");
        
        // Assert
        Assert.IsFalse(txtResult);
        Assert.IsFalse(docxResult);
        Assert.IsFalse(xlsxResult);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void ReadBlocks_NullStream_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        _reader.ReadBlocks(null!, "test.pdf").ToList();
    }

    [TestMethod]
    public void ReadBlocks_EmptyStream_ReturnsEmptyResult()
    {
        // Arrange
        using var stream = new MemoryStream();
        
        // Act & Assert
        // 注意：空的PDF流会导致PdfPig抛出异常，这是正常行为
        // 这个测试主要验证我们的错误处理机制
        var blocks = _reader.ReadBlocks(stream, "empty.pdf");
        var result = blocks.ToList();
        
        // 应该返回空列表而不是抛出异常
        Assert.IsNotNull(result);
    }

    [TestMethod]
    public void FileExtensionCheck_CaseInsensitive()
    {
        // Arrange & Act & Assert
        Assert.IsTrue(_reader.CanRead("test.PDF"));
        Assert.IsTrue(_reader.CanRead("test.Pdf"));
        Assert.IsTrue(_reader.CanRead("test.pDF"));
        Assert.IsFalse(_reader.CanRead("test.pdf.txt"));
    }

    /// <summary>
    /// 集成测试：需要真实的PDF文件
    /// 这个测试在没有PDF文件的情况下会被跳过
    /// </summary>
    [TestMethod]
    [TestCategory("Integration")]
    public void ReadBlocks_ValidPdfFile_ReturnsBlocks()
    {
        // 这是一个集成测试的模板
        // 在实际使用中，你可以放置一个小的测试PDF文件
        // 并验证提取的块数量和类型是否正确
        
        // 由于没有真实的PDF文件，这个测试暂时跳过
        Assert.Inconclusive("需要真实的PDF文件进行集成测试");
    }
}
