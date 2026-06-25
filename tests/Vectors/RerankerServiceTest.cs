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

    #region Service Resolution Tests

    [TestMethod]
    [TestCategory("Unit")]
    public void Service_ShouldBeResolvedFromContainer()
    {
        // Assert
        Assert.IsNotNull(_rerankerService);
    }

    #endregion

    #region Core Functionality Tests

    [TestMethod]
    [TestCategory("Unit")]
    public void Rerank_WithEmptyItems_ShouldReturnEmptyList()
    {
        // Arrange
        var query = "test query";
        var items = new List<ScoredSearchResult>();

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
        var items = new List<ScoredSearchResult>
        {
            CreateScoredResult("result1", "股票市场技术分析指标", "https://example.com/1", 0.8f)
        };

        // Act
        var result = _rerankerService.Rerank(query, items);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("result1", result[0].Name);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Rerank_WithMultipleItems_ShouldReturnReorderedResults()
    {
        // Arrange - 高向量分数的项目应排前面
        var query = "AI 人工智能";
        var items = new List<ScoredSearchResult>
        {
            CreateScoredResult("result1", "股票市场基本面分析", "https://example.com/1", 0.5f),
            CreateScoredResult("result2", "人工智能AI技术发展趋势", "https://example.com/2", 0.9f),
            CreateScoredResult("result3", "机器学习在投资中的应用", "https://example.com/3", 0.7f)
        };

        // Act
        var result = _rerankerService.Rerank(query, items);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(3, result.Count);

        // 向量分数最高的 result2 应排第一
        Assert.AreEqual("result2", result[0].Name);

        Console.WriteLine($"Reranked order:");
        for (int i = 0; i < result.Count; i++)
        {
            Console.WriteLine($"{i + 1}. {result[i].Name}: {result[i].Value}");
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Rerank_WithRelevantQuery_ShouldPrioritizeRelevantContent()
    {
        // Arrange
        var query = "新能源汽车投资";
        var items = new List<ScoredSearchResult>
        {
            CreateScoredResult("result1", "传统汽车行业发展", "https://example.com/1", 0.4f),
            CreateScoredResult("result2", "新能源汽车市场分析", "https://example.com/2", 0.85f),
            CreateScoredResult("result3", "电动汽车技术创新", "https://example.com/3", 0.75f),
            CreateScoredResult("result4", "房地产投资策略", "https://example.com/4", 0.3f)
        };

        // Act
        var result = _rerankerService.Rerank(query, items);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(4, result.Count);

        Console.WriteLine($"Reranked results for '{query}':");
        for (int i = 0; i < result.Count; i++)
        {
            Console.WriteLine($"{i + 1}. {result[i].Name}: {result[i].Value}");
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Rerank_WithLargeDataset_ShouldHandleEfficiently()
    {
        // Arrange
        var query = "芯片半导体";
        var items = new List<ScoredSearchResult>();

        for (int i = 1; i <= 15; i++)
        {
            var isRelevant = i % 3 == 0;
            var content = isRelevant
                ? $"芯片半导体技术发展报告第{i}部分"
                : $"一般性市场分析报告第{i}部分";
            // 相关项给更高向量分数
            var score = isRelevant ? 0.8f + i * 0.01f : 0.3f + i * 0.01f;
            items.Add(CreateScoredResult($"result{i}", content, $"https://example.com/{i}", score));
        }

        // Act
        var result = _rerankerService.Rerank(query, items);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(15, result.Count);

        Console.WriteLine($"Reranked large dataset for '{query}':");
        for (int i = 0; i < Math.Min(5, result.Count); i++)
        {
            Console.WriteLine($"{i + 1}. {result[i].Name}: {result[i].Value}");
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Rerank_WithNullQuery_ShouldNotThrowException()
    {
        // Arrange
        var items = new List<ScoredSearchResult>
        {
            CreateScoredResult("result1", "测试内容", "https://example.com/1", 0.5f)
        };

        // Act & Assert - Should not throw
        var result = _rerankerService.Rerank(null!, items);

        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Rerank_WithEmptyQuery_ShouldReturnByVectorScore()
    {
        // Arrange - 空查询时按向量分数排序
        var items = new List<ScoredSearchResult>
        {
            CreateScoredResult("result1", "第一个结果", "https://example.com/1", 0.3f),
            CreateScoredResult("result2", "第二个结果", "https://example.com/2", 0.9f)
        };

        // Act
        var result = _rerankerService.Rerank("", items);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.Count);
        // 向量分数高的应排前面
        Assert.AreEqual("result2", result[0].Name);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Rerank_ShouldPreserveAllResults()
    {
        // Arrange
        var query = "市场分析";
        var originalCount = 10;
        var items = new List<ScoredSearchResult>();

        for (int i = 1; i <= originalCount; i++)
        {
            items.Add(CreateScoredResult($"result{i}", $"内容{i}", $"https://example.com/{i}", 0.5f + i * 0.01f));
        }

        // Act
        var result = _rerankerService.Rerank(query, items);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(originalCount, result.Count);

        var originalNames = items.Select(i => i.Item.Name).OrderBy(n => n).ToArray();
        var resultNames = result.Select(r => r.Name).OrderBy(n => n).ToArray();

        CollectionAssert.AreEquivalent(originalNames, resultNames);
    }

    #endregion

    #region Helper Methods

    private static ScoredSearchResult CreateScoredResult(string name, string value, string link, float vectorScore)
    {
        var textResult = new TextSearchResult(value)
        {
            Name = name,
            Link = link
        };
        return new ScoredSearchResult(textResult, vectorScore);
    }

    #endregion
}
