using MarketAssistant.Rag;
using MarketAssistant.Rag.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;

namespace TestMarketAssistant.Vectors;

/// <summary>
/// RetrievalOrchestrator 集成测试 - 验证RetrieveAsync能否正常工作
/// </summary>
[TestClass]
public class RetrievalOrchestratorIntegrationTest : BaseAgentTest
{
    private IRetrievalOrchestrator _retrievalOrchestrator = null!;
    private IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator = null!;
    private VectorStore _vectorStore = null!;
    private string _collectionName = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        base.BaseInitialize();

        // 从 DI 容器获取所有服务
        _embeddingGenerator = _serviceProvider.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();
        _vectorStore = _serviceProvider.GetRequiredService<VectorStore>();
        _retrievalOrchestrator = _serviceProvider.GetRequiredService<IRetrievalOrchestrator>();

        // 创建测试集合并添加测试数据
        _collectionName = $"test_retrieval_{Guid.NewGuid():N}";
        var testCollection = _vectorStore.GetCollection<string, TextParagraph>(_collectionName);
        await testCollection.EnsureCollectionExistsAsync();

        // 手动添加一些测试段落
        await AddTestDataAsync(testCollection);

        Console.WriteLine($"测试数据准备完成，集合: {_collectionName}");
    }

    private async Task AddTestDataAsync(VectorStoreCollection<string, TextParagraph> collection)
    {
        var testParagraphs = new[]
        {
            new TextParagraph
            {
                Key = "test1",
                DocumentUri = "test://doc1",
                ParagraphId = "para1",
                Text = "股票市场分析是投资决策的重要依据。通过技术分析和基本面分析，可以评估股票的投资价值。",
                Order = 0,
                Section = "投资分析",
                SourceType = "test",
                ContentHash = "hash1",
                PublishedAt = DateTimeOffset.UtcNow.ToString("O"),
                BlockKind = 0,
                HeadingLevel = null,
                ListType = null,
                ImageUri = null
            },
            new TextParagraph
            {
                Key = "test2",
                DocumentUri = "test://doc1",
                ParagraphId = "para2",
                Text = "财务报表分析包括资产负债表、利润表和现金流量表的分析。这些报表反映了企业的财务状况。",
                Order = 1,
                Section = "财务分析",
                SourceType = "test",
                ContentHash = "hash2",
                PublishedAt = DateTimeOffset.UtcNow.ToString("O"),
                BlockKind = 0,
                HeadingLevel = null,
                ListType = null,
                ImageUri = null
            },
            new TextParagraph
            {
                Key = "test3",
                DocumentUri = "test://doc2",
                ParagraphId = "para3",
                Text = "市场趋势分析有助于识别投资机会。技术指标如移动平均线、RSI等可以辅助判断买卖时机。",
                Order = 0,
                Section = "技术分析",
                SourceType = "test",
                ContentHash = "hash3",
                PublishedAt = DateTimeOffset.UtcNow.ToString("O"),
                BlockKind = 0,
                HeadingLevel = null,
                ListType = null,
                ImageUri = null
            }
        };

        // 为每个段落生成嵌入并存储
        foreach (var paragraph in testParagraphs)
        {
            paragraph.TextEmbedding = await _embeddingGenerator.GenerateAsync(paragraph.Text);
            paragraph.ImageEmbedding = new Embedding<float>(new float[1024]); // 空的图像嵌入
            await collection.UpsertAsync(paragraph);
        }
    }

    [TestMethod]
    public async Task TestRetrieveAsync()
    {
        // 验证RetrieveAsync能否正常工作
        var query = "股票投资分析";

        Console.WriteLine($"执行检索查�? '{query}'");

        // 执行检�?
        var results = await _retrievalOrchestrator.RetrieveAsync(
            query,
            _collectionName,
            top: 5);

        // 验证结果
        Assert.IsNotNull(results, "检索结果不应为null");
        Assert.IsTrue(results.Count > 0, "应该返回至少一个结果");
        Assert.IsTrue(results.Count <= 5, "结果数量不应超过请求的top数");

        Console.WriteLine($"检索完成，返回 {results.Count} 个结果");

        // 验证结果的质量
        foreach (var result in results)
        {
            Assert.IsNotNull(result.Value, "结果内容不应为null");
            Assert.IsTrue(result.Value.Length > 0, "结果内容不应为空");
            Assert.IsNotNull(result.Name, "结果名称不应为null");

            var preview = result.Value.Length > 100 ? result.Value[..100] + "..." : result.Value;
            Console.WriteLine($"- {result.Name}: {preview}");
        }
    }
}
