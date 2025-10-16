using MarketAssistant.Rag.Interfaces;
using MarketAssistant.Rag.Services;
using Microsoft.Extensions.DependencyInjection;

namespace TestMarketAssistant.Vectors;

[TestClass]
public class MarkdownDocumentBlockReaderTest : BaseKernelTest
{
    private MarkdownDocumentBlockReader _reader = null!;
    private string _testMarkdownContent = null!;
    private string _testFilePath = null!;

    [TestInitialize]
    public void Setup()
    {
        base.BaseInitialize();

        // Get MarkdownDocumentBlockReader from DI container
        _reader = _kernel.Services.GetRequiredService<MarkdownDocumentBlockReader>();

        // 创建测试 Markdown 内容
        _testMarkdownContent = """
## 测试文档

1. 列表1
2. 列表2
3. 列表3

### 一、引言

在当今数字化信息爆炸的时代，数据的呈现形式丰富多样，图片和表格作为直观且高效的信息载体，被广泛应用于各个领域。

### 二、图片展示

#### （一）自然风光图片

![文档图片3](file:///C:/Users/mayue/Documents/MarketAssistant/Images/doc_image3.png)

这张图片展示了雄伟壮观的山脉景色。

#### （二）城市建筑图片

![文档图片2](file:///C:/Users/mayue/Documents/MarketAssistant/Images/doc_image2.png)

此图片聚焦于现代化的城市建筑。

#### （三）动物生活图片

![文档图片1](file:///C:/Users/mayue/Documents/MarketAssistant/Images/doc_image1.png)

图片中的动物十分可爱。

### 三、表格信息

| **地区** | **著名景点数量** | **年游客接待量（万人次）** |
| --- | --- | --- |
| A地区 | 15 | 500 |
| B地区 | 20 | 800 |
| C地区 | 12 | 300 |

### 四、结论

通过本测试文档中图片和表格的结合展示，我们可以看到"一带多"的信息呈现方式能够有效地传达丰富的信息。
""";

        // 创建临时测试文件
        _testFilePath = Path.Combine(Path.GetTempPath(), $"test_markdown_with_images_{Guid.NewGuid()}.md");
        File.WriteAllText(_testFilePath, _testMarkdownContent);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (File.Exists(_testFilePath))
            {
                File.Delete(_testFilePath);
            }
        }
        catch
        {
            // 忽略删除错误
        }
    }

    [TestMethod]
    public async Task ReadBlocksAsync_ShouldExtractImageBlocks()
    {
        // Arrange & Act
        var blocks = await _reader.ReadBlocksAsync(_testFilePath);
        var blocksList = blocks.ToList();

        // Assert
        Assert.IsNotNull(blocks);
        Assert.IsTrue(blocksList.Count > 0, "应该提取到至少一个块");

        // 查找图片块
        var imageBlocks = blocksList.OfType<ImageBlock>().ToList();
        Assert.IsTrue(imageBlocks.Count > 0, "应该提取到至少一个图片块");

        // 验证图片块的内容
        foreach (var imageBlock in imageBlocks)
        {
            Assert.IsNotNull(imageBlock.Description, "图片块应该有描述");
            Assert.IsTrue(!string.IsNullOrEmpty(imageBlock.Description), "图片描述不应为空");

            // 验证图片块的序号
            Assert.IsTrue(imageBlock.Order >= 0, "图片块应该有有效的序号");

            // 打印调试信息
            System.Diagnostics.Debug.WriteLine($"发现图片块: Order={imageBlock.Order}, Description='{imageBlock.Description}', Caption='{imageBlock.Caption}', ImageBytes.Length={imageBlock.ImageBytes?.Length ?? 0}");
        }

        // 验证具体的图片数量（应该有3张图片）
        Assert.AreEqual(3, imageBlocks.Count, $"应该提取到3个图片块，实际提取到{imageBlocks.Count}个");

        // 验证图片描述
        var descriptions = imageBlocks.Select(img => img.Description).ToList();
        Assert.IsTrue(descriptions.Contains("文档图片1"), "应该包含'文档图片1'");
        Assert.IsTrue(descriptions.Contains("文档图片2"), "应该包含'文档图片2'");
        Assert.IsTrue(descriptions.Contains("文档图片3"), "应该包含'文档图片3'");
    }

    [TestMethod]
    public async Task ReadBlocksAsync_ShouldExtractHeadingBlocks()
    {
        // Arrange & Act
        var blocks = await _reader.ReadBlocksAsync(_testFilePath);
        var blocksList = blocks.ToList();

        // Assert - 验证标题块
        var headingBlocks = blocksList.OfType<HeadingBlock>().ToList();
        Assert.IsTrue(headingBlocks.Count > 0, "应该提取到至少一个标题块");

        // 查找特定标题
        var testDocHeading = headingBlocks.FirstOrDefault(h => h.Text.Contains("测试文档"));
        Assert.IsNotNull(testDocHeading, "应该找到'测试文档'标题");
        Assert.AreEqual(2, testDocHeading.Level, "测试文档应该是2级标题");
    }

    [TestMethod]
    public async Task ReadBlocksAsync_ShouldExtractTableBlocks()
    {
        // Arrange & Act
        var blocks = await _reader.ReadBlocksAsync(_testFilePath);
        var blocksList = blocks.ToList();

        // Assert - 验证表格块
        var tableBlocks = blocksList.OfType<TableBlock>().ToList();
        Assert.IsTrue(tableBlocks.Count > 0, "应该提取到至少一个表格块");

        var tableBlock = tableBlocks.First();
        Assert.IsNotNull(tableBlock.Rows, "表格应该有行数据");
        Assert.IsTrue(tableBlock.Rows.Count > 0, "表格应该至少有一行数据");
    }

    [TestMethod]
    public async Task ReadBlocksAsync_ShouldExtractListBlocks()
    {
        // Arrange & Act
        var blocks = await _reader.ReadBlocksAsync(_testFilePath);
        var blocksList = blocks.ToList();

        // Assert - 验证列表块
        var listBlocks = blocksList.OfType<ListBlock>().ToList();
        Assert.IsTrue(listBlocks.Count > 0, "应该提取到至少一个列表块");

        var listBlock = listBlocks.First();
        Assert.AreEqual(ListType.Ordered, listBlock.ListType, "应该是有序列表");
        Assert.IsTrue(listBlock.Items.Count >= 3, "列表应该至少有3个项目");
        Assert.IsTrue(listBlock.Items.Any(item => item.Contains("列表1")), "应该包含'列表1'");
    }

    [TestMethod]
    public async Task ReadBlocksAsync_ShouldExtractTextBlocks()
    {
        // Arrange & Act
        var blocks = await _reader.ReadBlocksAsync(_testFilePath);
        var blocksList = blocks.ToList();

        // Assert - 验证文本块
        var textBlocks = blocksList.OfType<TextBlock>().ToList();
        Assert.IsTrue(textBlocks.Count > 0, "应该提取到至少一个文本块");

        // 查找特定文本
        var introText = textBlocks.FirstOrDefault(t => t.Text.Contains("数字化信息爆炸"));
        Assert.IsNotNull(introText, "应该找到包含'数字化信息爆炸'的文本块");
    }

    [TestMethod]
    public async Task ReadBlocksAsync_ShouldMaintainCorrectOrder()
    {
        // Arrange & Act
        var blocks = await _reader.ReadBlocksAsync(_testFilePath);
        var blocksList = blocks.ToList();

        // Assert - 验证块的顺序
        for (int i = 0; i < blocksList.Count - 1; i++)
        {
            Assert.IsTrue(blocksList[i].Order <= blocksList[i + 1].Order,
                $"块的顺序应该是递增的，但发现 Order[{i}]={blocksList[i].Order} > Order[{i + 1}]={blocksList[i + 1].Order}");
        }
    }
}
