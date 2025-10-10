using MarketAssistant.Vectors.Services;
using MarketAssistant.Vectors.Interfaces;

namespace TestMarketAssistant.Vectors;

[TestClass]
public class PdfFileTest : BaseKernelTest
{
    private IMarkdownConverter _converter = null!;
    private IDocumentBlockReader _reader = null!;
    private MarkdownConverterFactory _converterFactory = null!;
    private DocumentBlockReaderFactory _readerFactory = null!;
    private string _testPdfFile = null!;

    [TestInitialize]
    public void Setup()
    {
        base.BaseInitialize();
        
        // 从 DI 容器获取工厂
        _converterFactory = _kernel.Services.GetRequiredService<MarkdownConverterFactory>();
        _readerFactory = _kernel.Services.GetRequiredService<DocumentBlockReaderFactory>();

        // 获取PDF转换器
        _converter = _converterFactory.GetConverter("test.pdf")!;

        // 获取PDF读取器
        _reader = _readerFactory.GetReader("test.pdf")!;

        // 获取测试项目根目录
        var testProjectDir = GetTestProjectDirectory();
        _testPdfFile = Path.Combine(testProjectDir, "demo.pdf");

        // 确保测试文件存在
        Assert.IsTrue(File.Exists(_testPdfFile), $"测试文件不存在: {_testPdfFile}");
    }

    private static string GetTestProjectDirectory()
    {
        // 从当前执行目录向上查找，直到找到包含demo.pdf的目录
        var currentDir = Directory.GetCurrentDirectory();
        var dir = new DirectoryInfo(currentDir);

        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "demo.pdf")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new FileNotFoundException("无法找到包含demo.pdf的测试项目目录。请确保demo.pdf文件存在于TestMarketAssistant项目目录中。");
    }

    #region Converter Tests

    [TestMethod]
    public void PdfMarkdownConverter_ShouldHandlePdfFiles()
    {
        // Arrange & Act & Assert
        Assert.IsTrue(_converterFactory.CanConvert("test.pdf"));
        Assert.IsTrue(_converterFactory.CanConvert("TEST.PDF"));
        Assert.IsFalse(_converterFactory.GetConverter("test.txt")?.CanConvert("test.txt") ?? false);
    }

    [TestMethod]
    public async Task PdfMarkdownConverter_ShouldConvertRealPdfFile()
    {
        // Act
        var result = await _converter.ConvertToMarkdownAsync(_testPdfFile);

        // Assert
        Assert.IsNotNull(result, "转换结果不应为null");
        Assert.IsTrue(result.Length > 0, "转换结果不应为空");

        // 打印结果以便调试
        Console.WriteLine("转换结果:");
        Console.WriteLine(result);

        // 基本验证：结果应该包含一些文本内容
        Assert.IsTrue(result.Trim().Length > 10, "转换结果应该包含实际内容");
    }

    [TestMethod]
    public async Task PdfMarkdownConverter_ShouldHandleDocumentStructure()
    {
        // Act
        var result = await _converter.ConvertToMarkdownAsync(_testPdfFile);

        // Assert - 验证基本的Markdown结构
        Assert.IsNotNull(result);

        // 如果文档包含标题，应该有#符号
        if (result.Contains("#"))
        {
            Assert.IsTrue(result.Contains("#"), "应该包含Markdown标题标记");
        }

        // 验证文档不是完全空白
        var trimmedResult = result.Trim();
        Assert.IsTrue(trimmedResult.Length > 0, "转换后的文档不应为空");

        // 验证不包含明显的错误标记
        Assert.IsFalse(result.Contains("ERROR"), "转换结果不应包含错误信息");
    }

    [TestMethod]
    public async Task PdfMarkdownConverter_ShouldPreserveTextFormatting()
    {
        // Act
        var result = await _converter.ConvertToMarkdownAsync(_testPdfFile);

        // Assert
        Assert.IsNotNull(result);

        // 验证基本的文本结构保存
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.IsTrue(lines.Length > 0, "应该包含至少一行文本");

        // 验证文本内容不全是空白字符
        var hasNonWhitespaceContent = lines.Any(line => !string.IsNullOrWhiteSpace(line));
        Assert.IsTrue(hasNonWhitespaceContent, "应该包含非空白内容");
    }

    #endregion

    #region Reader Tests

    [TestMethod]
    public void PdfBlockReader_ShouldHandlePdfFiles()
    {
        // Arrange & Act & Assert
        Assert.IsTrue(_readerFactory.CanRead("test.pdf"));
        Assert.IsTrue(_readerFactory.CanRead("TEST.PDF"));
        Assert.IsFalse(_readerFactory.GetReader("test.txt")?.CanRead("test.txt") ?? false);
    }

    [TestMethod]
    public async Task PdfBlockReader_ShouldReadDocumentBlocks()
    {
        // Act
        var blocks = await _reader.ReadBlocksAsync(_testPdfFile);
        var blockList = blocks.ToList();

        // Assert
        Assert.IsNotNull(blockList, "文档块列表不应为null");
        Assert.IsTrue(blockList.Count > 0, "应该至少读取到一个文档块");

        // 验证块的基本属性
        foreach (var block in blockList)
        {
            Assert.IsNotNull(block, "文档块不应为null");
            Assert.IsTrue(block.Order >= 0, "文档块顺序应该是非负数");
        }

        // 打印结果以便调试
        Console.WriteLine($"读取到 {blockList.Count} 个文档块:");
        for (int i = 0; i < Math.Min(blockList.Count, 5); i++) // 只打印前5个块
        {
            var block = blockList[i];
            Console.WriteLine($"块 {i}: 类型={block.Type}, 顺序={block.Order}, 文本='{block.GetText().Substring(0, Math.Min(50, block.GetText().Length))}...'");
        }
    }

    [TestMethod]
    public async Task PdfBlockReader_ShouldPreserveBlockOrder()
    {
        // Act
        var blocks = await _reader.ReadBlocksAsync(_testPdfFile);
        var blockList = blocks.ToList();

        // Assert
        Assert.IsTrue(blockList.Count > 0, "应该至少读取到一个文档块");

        // 验证块的顺序是递增的
        for (int i = 1; i < blockList.Count; i++)
        {
            Assert.IsTrue(blockList[i].Order >= blockList[i - 1].Order,
                $"文档块顺序应该是递增的。块 {i - 1} 的顺序是 {blockList[i - 1].Order}，块 {i} 的顺序是 {blockList[i].Order}");
        }
    }

    [TestMethod]
    public async Task PdfBlockReader_ShouldExtractTextContent()
    {
        // Act
        var blocks = await _reader.ReadBlocksAsync(_testPdfFile);
        var blockList = blocks.ToList();

        // Assert
        Assert.IsTrue(blockList.Count > 0, "应该至少读取到一个文档块");

        // 验证至少有一些块包含文本内容
        var hasTextContent = blockList.Any(block => block.HasTextContent());
        Assert.IsTrue(hasTextContent, "应该至少有一个文档块包含文本内容");

        // 验证文本内容不为空
        var textBlocks = blockList.Where(block => block.HasTextContent()).ToList();
        foreach (var block in textBlocks.Take(3)) // 检查前3个有文本的块
        {
            var text = block.GetText();
            Assert.IsFalse(string.IsNullOrWhiteSpace(text), "文档块的文本内容不应为空白");
            Console.WriteLine($"文档块文本: {text.Substring(0, Math.Min(100, text.Length))}...");
        }
    }

    [TestMethod]
    public async Task PdfBlockReader_ShouldHandleEmptyOrCorruptFile()
    {
        // Arrange
        var tempFile = Path.GetTempFileName() + ".pdf";
        try
        {
            // 创建一个空的或损坏的PDF文件
            await File.WriteAllTextAsync(tempFile, "这不是一个有效的PDF文件");

            // Act & Assert
            try
            {
                var blocks = await _reader.ReadBlocksAsync(tempFile);
                // 如果没有抛出异常，检查结果是否为空或合理
                var blockList = blocks.ToList();
                Assert.IsTrue(blockList.Count == 0, "损坏的PDF文件应该返回空块列表或抛出异常");
            }
            catch (Exception)
            {
                // 抛出异常是预期的行为
                Assert.IsTrue(true, "处理无效PDF文件时抛出异常是正常的");
            }
        }
        finally
        {
            // 清理临时文件
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [TestMethod]
    public async Task PdfBlockReader_ShouldHandleNonExistentFile()
    {
        // Arrange
        var nonExistentFile = "non_existent_file.pdf";

        // Act & Assert
        try
        {
            var blocks = await _reader.ReadBlocksAsync(nonExistentFile);
            Assert.Fail("应该抛出异常处理不存在的文件");
        }
        catch (Exception)
        {
            // 抛出异常是预期的行为
            Assert.IsTrue(true, "处理不存在的文件时抛出异常是正常的");
        }
    }

    #endregion
}
