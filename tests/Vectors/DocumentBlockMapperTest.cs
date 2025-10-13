using MarketAssistant.Rag.Interfaces;
using MarketAssistant.Rag.Services;
using Microsoft.Extensions.DependencyInjection;

namespace TestMarketAssistant.Vectors;

[TestClass]
public class DocumentBlockMapperTest : BaseKernelTest
{
    private ITextCleaningService _cleaningService = null!;
    private ITextChunkingService _chunkingService = null!;
    private DocumentBlockMapper _mapper = null!;

    [TestInitialize]
    public void Setup()
    {
        base.BaseInitialize();

        _cleaningService = _kernel.Services.GetRequiredService<ITextCleaningService>();
        _chunkingService = _kernel.Services.GetRequiredService<ITextChunkingService>();
        _mapper = new DocumentBlockMapper(_cleaningService, _chunkingService);
    }

    [TestMethod]
    public void MapBlock_TextBlock_ShouldCreateCorrectParagraphsWithConsistentIds()
    {
        // Arrange
        var textBlock = new TextBlock
        {
            Text = "这是一段很长的文本。",
            Order = 0
        };

        // Act
        var (paragraphs, nextOrder, updatedSection) = _mapper.MapBlock(
            textBlock, "test.pdf", 10, "第一章");

        // Assert
        var paragraphList = paragraphs.ToList();
        Assert.IsTrue(paragraphList.Count > 0, "应该生成至少一个段落");

        // 验证第一个段落的基本属性
        var firstParagraph = paragraphList[0];
        Assert.IsTrue(firstParagraph.ParagraphId.StartsWith("txt_"), "文本块的ParagraphId应该以txt_开头");
        Assert.AreEqual(10, firstParagraph.Order);
        Assert.AreEqual<int?>(0, firstParagraph.BlockKind); // Text
        Assert.AreEqual("第一章", firstParagraph.Section);

        // 验证下一个序号正确递增
        Assert.IsTrue(nextOrder > 10, "下一个序号应该大于起始序号");
        Assert.AreEqual("第一章", updatedSection); // 章节保持不变
    }

    [TestMethod]
    public void MapBlock_HeadingBlock_ShouldCreateCorrectParagraph()
    {
        // Arrange
        var headingBlock = new HeadingBlock
        {
            Text = "第一章 概述",
            Level = 1,
            Order = 0
        };

        // Act
        var (paragraphs, nextOrder, updatedSection) = _mapper.MapBlock(
            headingBlock, "test.pdf", 0, null);

        // Assert
        var paragraph = paragraphs.Single();
        Assert.AreEqual<int?>(1, paragraph.BlockKind); // Heading
        Assert.AreEqual(1, paragraph.HeadingLevel);
        Assert.IsTrue(paragraph.Text.Contains("第一章"), "标题文本应该包含原始内容");
        Assert.IsTrue(paragraph.ParagraphId.StartsWith("hdg_"), "标题块的ParagraphId应该以hdg_开头");
        Assert.AreEqual("第一章 概述", updatedSection); // 应该更新章节
        Assert.AreEqual(1, nextOrder);
    }

    [TestMethod]
    public void MapBlock_ListBlock_ShouldCreateCorrectParagraph()
    {
        // Arrange
        var listBlock = new ListBlock
        {
            Items = new[] { "项目一", "项目二", "项目三" },
            ListType = ListType.Unordered,
            Order = 0
        };

        // Act
        var (paragraphs, nextOrder, updatedSection) = _mapper.MapBlock(
            listBlock, "test.pdf", 0, "第一章");

        // Assert
        var paragraph = paragraphs.Single();
        Assert.AreEqual<int?>(2, paragraph.BlockKind); // List
        Assert.AreEqual<int?>(0, paragraph.ListType); // Unordered
        Assert.AreEqual("第一章", paragraph.Section);
        Assert.IsTrue(paragraph.ParagraphId.StartsWith("lst_"), "列表块的ParagraphId应该以lst_开头");
        Assert.AreEqual(1, nextOrder);
    }

    [TestMethod]
    public void MapBlock_TableBlock_ShouldCreateCorrectParagraph()
    {
        // Arrange
        var tableBlock = new TableBlock
        {
            Rows = new List<IReadOnlyList<string>>
            {
                new[] { "列1", "列2" },
                new[] { "值1", "值2" }
            },
            Markdown = "| 列1 | 列2 |\n|-----|-----|\n| 值1 | 值2 |",
            Hash = "table_hash_123",
            Order = 0
        };

        // Act
        var (paragraphs, nextOrder, updatedSection) = _mapper.MapBlock(
            tableBlock, "test.pdf", 0, "第一章");

        // Assert
        var paragraph = paragraphs.Single();
        Assert.AreEqual<int?>(3, paragraph.BlockKind); // Table
        Assert.AreEqual("第一章", paragraph.Section);
        Assert.AreEqual("table_hash_123", paragraph.ContentHash);
        Assert.IsTrue(paragraph.ParagraphId.StartsWith("tbl_"), "表格块的ParagraphId应该以tbl_开头");
        Assert.AreEqual(1, nextOrder);
    }

    [TestMethod]
    public void MapBlock_ImageBlock_ShouldCreateCorrectParagraph()
    {
        // Arrange
        var imageBlock = new ImageBlock
        {
            ImageBytes = new byte[] { 1, 2, 3, 4, 5 },
            Description = "图片描述",
            Order = 0
        };

        var imageMetadata = new ImageMetadata(
            Caption: "这是一张图片",
            StoredPath: "/images/test.jpg",
            ImageEmbedding: new Microsoft.Extensions.AI.Embedding<float>(new float[1024])
        );

        // Act
        var (paragraphs, nextOrder, updatedSection) = _mapper.MapBlock(
            imageBlock, "test.pdf", 5, "第二章", imageMetadata);

        // Assert
        var paragraph = paragraphs.Single();
        Assert.AreEqual<int?>(4, paragraph.BlockKind); // Image
        Assert.AreEqual("第二章", paragraph.Section);
        Assert.AreEqual("这是一张图片", paragraph.Text);
        Assert.AreEqual("/images/test.jpg", paragraph.ImageUri);
        Assert.IsTrue(paragraph.ParagraphId.StartsWith("img_"), "图片块的ParagraphId应该以img_开头");
        Assert.AreEqual(6, nextOrder);
        Assert.IsNotNull(paragraph.ImageEmbedding);
    }
}
