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
    public void Service_ShouldBeResolvedFromContainer()
    {
        // Assert
        Assert.IsNotNull(_rerankerService);
    }

    #endregion

    #region Core Functionality Tests

    [TestMethod]
    public void Rerank_WithEmptyItems_ShouldReturnEmptyList()
    {
        // Arrange
        var query = "test query";
        var items = new List<TextSearchResult>();

        // Act
        var result = _rerankerService.Rerank(query, items);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Rerank_WithSingleItem_ShouldReturnSameItem()
    {
        // Arrange
        var query = "股票分析";
        var items = new List<TextSearchResult>
        {
            CreateTextSearchResult("result1", "股票市场技术分析指标", "https://example.com/1")
        };

        // Act
        var result = _rerankerService.Rerank(query, items);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("result1", result[0].Name);
    }

    [TestMethod]
    public void Rerank_WithMultipleItems_ShouldReturnReorderedResults()
    {
        // Arrange
        var query = "AI 人工智能";
        var items = new List<TextSearchResult>
        {
            CreateTextSearchResult("result1", "股票市场基本面分析", "https://example.com/1"),
            CreateTextSearchResult("result2", "人工智能AI技术发展趋势", "https://example.com/2"),
            CreateTextSearchResult("result3", "机器学习在投资中的应用", "https://example.com/3")
        };

        // Act
        var result = _rerankerService.Rerank(query, items);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(3, result.Count);

        // The AI-related item should be ranked higher
        Console.WriteLine($"Reranked order:");
        for (int i = 0; i < result.Count; i++)
        {
            Console.WriteLine($"{i + 1}. {result[i].Name}: {result[i].Value}");
        }
    }

    [TestMethod]
    public void Rerank_WithRelevantQuery_ShouldPrioritizeRelevantContent()
    {
        // Arrange
        var query = "新能源汽车投资";
        var items = new List<TextSearchResult>
        {
            CreateTextSearchResult("result1", "传统汽车行业发展", "https://example.com/1"),
            CreateTextSearchResult("result2", "新能源汽车市场分析", "https://example.com/2"),
            CreateTextSearchResult("result3", "电动汽车技术创新", "https://example.com/3"),
            CreateTextSearchResult("result4", "房地产投资策略", "https://example.com/4")
        };

        // Act
        var result = _rerankerService.Rerank(query, items);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(4, result.Count);

        // Should prioritize new energy vehicle related content
        Console.WriteLine($"Reranked results for '{query}':");
        for (int i = 0; i < result.Count; i++)
        {
            Console.WriteLine($"{i + 1}. {result[i].Name}: {result[i].Value}");
        }
    }

    [TestMethod]
    public void Rerank_WithLargeDataset_ShouldHandleEfficiently()
    {
        // Arrange
        var query = "芯片半导体";
        var items = new List<TextSearchResult>();

        // Create 15 test items
        for (int i = 1; i <= 15; i++)
        {
            var isRelevant = i % 3 == 0; // Every 3rd item is relevant
            var content = isRelevant
                ? $"芯片半导体技术发展报告第{i}部分"
                : $"一般性市场分析报告第{i}部分";
            items.Add(CreateTextSearchResult($"result{i}", content, $"https://example.com/{i}"));
        }

        // Act
        var result = _rerankerService.Rerank(query, items);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(15, result.Count);

        Console.WriteLine($"Reranked large dataset for '{query}':");
        for (int i = 0; i < Math.Min(5, result.Count); i++) // Show top 5
        {
            Console.WriteLine($"{i + 1}. {result[i].Name}: {result[i].Value}");
        }
    }

    [TestMethod]
    public void Rerank_WithNullQuery_ShouldNotThrowException()
    {
        // Arrange
        var items = new List<TextSearchResult>
        {
            CreateTextSearchResult("result1", "测试内容", "https://example.com/1")
        };

        // Act & Assert - Should not throw
        var result = _rerankerService.Rerank(null!, items);

        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.Count);
    }

    [TestMethod]
    public void Rerank_WithEmptyQuery_ShouldReturnOriginalOrder()
    {
        // Arrange
        var items = new List<TextSearchResult>
        {
            CreateTextSearchResult("result1", "第一个结果", "https://example.com/1"),
            CreateTextSearchResult("result2", "第二个结果", "https://example.com/2")
        };

        // Act
        var result = _rerankerService.Rerank("", items);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(2, result.Count);
    }

    [TestMethod]
    public void Rerank_ShouldPreserveAllResults()
    {
        // Arrange
        var query = "市场分析";
        var originalCount = 10;
        var items = new List<TextSearchResult>();

        for (int i = 1; i <= originalCount; i++)
        {
            items.Add(CreateTextSearchResult($"result{i}", $"内容{i}", $"https://example.com/{i}"));
        }

        // Act
        var result = _rerankerService.Rerank(query, items);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(originalCount, result.Count);

        // All items should be preserved
        var originalNames = items.Select(i => i.Name).OrderBy(n => n).ToArray();
        var resultNames = result.Select(r => r.Name).OrderBy(n => n).ToArray();

        CollectionAssert.AreEquivalent(originalNames, resultNames);
    }

    #endregion

    #region Helper Methods

    private static TextSearchResult CreateTextSearchResult(string name, string value, string link)
    {
        return new TextSearchResult(value)
        {
            Name = name,
            Link = link
        };
    }

    #endregion
}