using MarketAssistant.Rag;
using MarketAssistant.Rag.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.Data;

namespace TestMarketAssistant.Vectors;

[TestClass]
public class RerankerServiceTest : BaseAgentTest
{
    private IRerankerService _rerankerService = null!;

    [TestInitialize]
    public void Initialize()
    {
        base.BaseInitialize();
        _rerankerService = _serviceProvider.GetRequiredService<IRerankerService>();
    }

    #region Core Functionality Tests

    [TestMethod]
    [TestCategory("Unit")]
    public void Rerank_WithEmptyItems_ShouldReturnEmptyList()
    {
        // Arrange
        var query = "test query";
        var items = new List<RagSearchCandidate>();

        // Act
        var result = _rerankerService.Rerank(query, items);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Rerank_WithSingleItem_ShouldReturnSameItem()
    {
        // Arrange
        var query = "股票分析";
        var items = new List<RagSearchCandidate>
        {
            CreateCandidate("result1", "股票市场技术分析指标", "https://example.com/1", 0.8f)
        };

        // Act
        var result = _rerankerService.Rerank(query, items);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("result1", result[0].Record.ParagraphId);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Rerank_WithMultipleItems_ShouldReturnReorderedResults()
    {
        // Arrange - 向量距离最小（最相关）的项目应排前面
        var query = "AI 人工智能";
        var items = new List<RagSearchCandidate>
        {
            CreateCandidate("result1", "股票市场基本面分析", "https://example.com/1", 0.5f),
            CreateCandidate("result2", "人工智能AI技术发展趋势", "https://example.com/2", 0.1f),
            CreateCandidate("result3", "机器学习在投资中的应用", "https://example.com/3", 0.3f)
        };

        // Act
        var result = _rerankerService.Rerank(query, items);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(3, result.Count);

        // 向量距离最小的 result2 应排第一
        Assert.AreEqual("result2", result[0].Record.ParagraphId);

        Console.WriteLine($"Reranked order:");
        for (int i = 0; i < result.Count; i++)
        {
            Console.WriteLine($"{i + 1}. {result[i].Record.ParagraphId}: {result[i].Record.Text}");
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Rerank_WithRelevantQuery_ShouldPrioritizeRelevantContent()
    {
        // Arrange - result2 与查询语义最相关（距离最小），result4 与查询无关（距离最大）
        var query = "新能源汽车投资";
        var items = new List<RagSearchCandidate>
        {
            CreateCandidate("result1", "传统汽车行业发展", "https://example.com/1", 0.6f),
            CreateCandidate("result2", "新能源汽车市场分析", "https://example.com/2", 0.15f),
            CreateCandidate("result3", "电动汽车技术创新", "https://example.com/3", 0.25f),
            CreateCandidate("result4", "房地产投资策略", "https://example.com/4", 0.7f)
        };

        // Act
        var result = _rerankerService.Rerank(query, items);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(4, result.Count);

        // 相关内容（result2）应排在无关内容（result4）之前
        var resultList = result.ToList();
        var relevantIndex = resultList.FindIndex(r => r.Record.ParagraphId == "result2");
        var irrelevantIndex = resultList.FindIndex(r => r.Record.ParagraphId == "result4");
        Assert.IsTrue(relevantIndex >= 0 && irrelevantIndex >= 0, "结果应包含所有输入项");
        Assert.IsTrue(relevantIndex < irrelevantIndex,
            $"相关内容 result2 应排在无关内容 result4 之前，实际顺序: {string.Join(", ", result.Select(r => r.Record.ParagraphId))}");

        Console.WriteLine($"Reranked results for '{query}':");
        for (int i = 0; i < result.Count; i++)
        {
            Console.WriteLine($"{i + 1}. {result[i].Record.ParagraphId}: {result[i].Record.Text}");
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Rerank_WithLargeDataset_ShouldPreserveAllItemsAndPreferRelevantOnes()
    {
        // Arrange - 相关项（i % 3 == 0）拥有更小的向量距离
        var query = "芯片半导体";
        var items = new List<RagSearchCandidate>();

        for (int i = 1; i <= 15; i++)
        {
            var isRelevant = i % 3 == 0;
            var content = isRelevant
                ? $"芯片半导体技术发展报告第{i}部分"
                : $"一般性市场分析报告第{i}部分";
            var distance = isRelevant ? 0.1f + i * 0.005f : 0.5f + i * 0.01f;
            items.Add(CreateCandidate($"result{i}", content, $"https://example.com/{i}", distance));
        }

        // Act
        var result = _rerankerService.Rerank(query, items);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(15, result.Count);

        // 所有输入项应保留
        var originalNames = items.Select(i => i.Record.ParagraphId).OrderBy(n => n).ToArray();
        var resultNames = result.Select(r => r.Record.ParagraphId).OrderBy(n => n).ToArray();
        CollectionAssert.AreEquivalent(originalNames, resultNames);

        // 相关项（result3/6/9/12/15）整体排名应优于无关项的中位排名
        var relevantRanks = result
            .Select((r, idx) => (ParagraphId: r.Record.ParagraphId, Rank: idx))
            .Where(t => t.ParagraphId is "result3" or "result6" or "result9" or "result12" or "result15")
            .Select(t => t.Rank)
            .ToList();
        var irrelevantRanks = result
            .Select((r, idx) => (ParagraphId: r.Record.ParagraphId, Rank: idx))
            .Where(t => t.ParagraphId is "result1" or "result2" or "result4" or "result5" or "result7")
            .Select(t => t.Rank)
            .ToList();
        Assert.IsTrue(relevantRanks.Average() < irrelevantRanks.Average(),
            $"相关项平均排名应优于无关项，相关项平均: {relevantRanks.Average():F2}，无关项平均: {irrelevantRanks.Average():F2}");

        Console.WriteLine($"Reranked large dataset for '{query}':");
        for (int i = 0; i < result.Count; i++)
        {
            Console.WriteLine($"{i + 1}. {result[i].Record.ParagraphId}: {result[i].Record.Text}");
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Rerank_WithNullQuery_ShouldNotThrowException()
    {
        // Arrange
        var items = new List<RagSearchCandidate>
        {
            CreateCandidate("result1", "测试内容", "https://example.com/1", 0.5f)
        };

        // Act & Assert - Should not throw
        var result = _rerankerService.Rerank(null!, items);

        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Rerank_WithEmptyQuery_ShouldReturnByVectorDistance()
    {
        // Arrange - 空查询时按向量距离排序（距离最小在前）
        var items = new List<RagSearchCandidate>
        {
            CreateCandidate("result1", "第一个结果", "https://example.com/1", 0.9f),
            CreateCandidate("result2", "第二个结果", "https://example.com/2", 0.1f)
        };

        // Act
        var result = _rerankerService.Rerank("", items);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.Count);
        // 向量距离最小的应排前面
        Assert.AreEqual("result2", result[0].Record.ParagraphId);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Rerank_WithEqualDistances_ShouldKeepStableOrder()
    {
        // Arrange - 所有距离相同，顺序应保持稳定
        var query = "";
        var items = new List<RagSearchCandidate>
        {
            CreateCandidate("first", "内容一", "https://example.com/1", 0.4f),
            CreateCandidate("second", "内容二", "https://example.com/2", 0.4f),
            CreateCandidate("third", "内容三", "https://example.com/3", 0.4f)
        };

        // Act
        var result = _rerankerService.Rerank(query, items);

        // Assert - 距离全部相同时不应颠倒原始顺序
        CollectionAssert.AreEqual(
            new[] { "first", "second", "third" },
            result.Select(r => r.Record.ParagraphId).ToArray());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Rerank_ShouldPreserveAllResults()
    {
        // Arrange
        var query = "市场分析";
        var originalCount = 10;
        var items = new List<RagSearchCandidate>();

        for (int i = 1; i <= originalCount; i++)
        {
            items.Add(CreateCandidate($"result{i}", $"内容{i}", $"https://example.com/{i}", 0.5f + i * 0.01f));
        }

        // Act
        var result = _rerankerService.Rerank(query, items);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(originalCount, result.Count);

        var originalNames = items.Select(i => i.Record.ParagraphId).OrderBy(n => n).ToArray();
        var resultNames = result.Select(r => r.Record.ParagraphId).OrderBy(n => n).ToArray();

        CollectionAssert.AreEquivalent(originalNames, resultNames);
    }

    #endregion

    #region Helper Methods

    private static RagSearchCandidate CreateCandidate(string name, string value, string link, float vectorDistance)
    {
        var record = new TextParagraph
        {
            Key = name,
            DocumentUri = link,
            ParagraphId = name,
            Text = value,
            Order = 0,
            SourceType = "test"
        };
        return new RagSearchCandidate(record, vectorDistance, string.Empty);
    }

    #endregion
}
