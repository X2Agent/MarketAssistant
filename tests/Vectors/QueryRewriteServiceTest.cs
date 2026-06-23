using MarketAssistant.Rag.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace TestMarketAssistant.Vectors;

[TestClass]
public class QueryRewriteServiceTest : BaseAgentTest
{
    private IQueryRewriteService _service = null!;

    [TestInitialize]
    public void Initialize()
    {
        base.BaseInitialize();
        _service = _serviceProvider.GetRequiredService<IQueryRewriteService>();
    }

    #region Service Resolution Tests

    [TestMethod]
    [TestCategory("Unit")]
    public void Service_ShouldBeResolvedFromContainer()
    {
        // Assert
        Assert.IsNotNull(_service);
    }

    #endregion

    #region Input Validation Tests


    [TestMethod]
    [TestCategory("Unit")]
    public void Rewrite_WithWhitespaceQuery_ShouldReturnEmptyList()
    {
        // Act
        var result = _service.Rewrite("   \t\n  ");

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Rewrite_WithZeroMaxCandidates_ShouldReturnEmptyList()
    {
        // Act
        var result = _service.Rewrite("test query", 0);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Rewrite_WithNegativeMaxCandidates_ShouldReturnEmptyList()
    {
        // Act
        var result = _service.Rewrite("test query", -5);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Count);
    }

    #endregion

    #region Functional Tests

    [TestMethod]
    [TestCategory("Unit")]
    public void Rewrite_WithValidQuery_ShouldReturnRewrittenQueries()
    {
        // Arrange
        var query = "股票市场分析";
        var expectedCandidates = 3;

        // Act
        var result = _service.Rewrite(query, expectedCandidates);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count > 0);
        Assert.IsTrue(result.Count <= expectedCandidates);

        // Verify that none of the results are empty or whitespace
        foreach (var rewrittenQuery in result)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(rewrittenQuery));
            Console.WriteLine($"Generated query: {rewrittenQuery}");
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Rewrite_WithDefaultMaxCandidates_ShouldReturnLimitedResults()
    {
        // Arrange
        var query = "AI人工智能投资";

        // Act
        var result = _service.Rewrite(query); // Using default maxCandidates = 3

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count <= 3); // Should be limited to default 3

        foreach (var rewrittenQuery in result)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(rewrittenQuery));
            Console.WriteLine($"Generated query: {rewrittenQuery}");
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Rewrite_WithSynonymExpansion_ShouldGenerateVariants()
    {
        // Arrange - 使用包含同义词的查询
        var query = "股票价格上涨";

        // Act
        var result = _service.Rewrite(query, 5);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count > 0);

        // Should generate variants with synonyms
        var allResults = string.Join(", ", result);
        Console.WriteLine($"All variants: {allResults}");

        foreach (var variant in result)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(variant));
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Rewrite_WithFinancialTerms_ShouldGenerateSynonymVariants()
    {
        // Arrange
        var query = "新能源股票投资";

        // Act
        var result = _service.Rewrite(query, 4);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count > 0);

        // 应通过同义词替换生成变体（如"股票"→"证券"/"股份"等）
        var hasSynonymVariant = result.Any(r =>
            r.Contains("证券") || r.Contains("股份") || r.Contains("股权") ||
            r.Contains("个股") || r.Contains("股价") ||
            r.Contains("电动车") || r.Contains("新能车") || r.Contains("锂电"));

        Assert.IsTrue(hasSynonymVariant, "应生成包含同义词替换的变体");

        foreach (var variant in result)
        {
            Console.WriteLine($"Generated variant: {variant}");
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Rewrite_WithTimeFrameTerms_ShouldGenerateSynonymVariants()
    {
        // Arrange
        var query = "芯片股票研究";

        // Act
        var result = _service.Rewrite(query, 6);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count > 0);

        // 应通过同义词替换生成变体
        var hasSynonymVariant = result.Any(r =>
            r.Contains("半导体") || r.Contains("集成电路") || r.Contains("处理器") ||
            r.Contains("证券") || r.Contains("股份") || r.Contains("个股"));

        Assert.IsTrue(hasSynonymVariant, "应生成包含同义词替换的变体");

        foreach (var variant in result)
        {
            Console.WriteLine($"Generated variant: {variant}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(variant));
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Rewrite_ShouldReturnUniqueResults()
    {
        // Arrange
        var query = "市场趋势分析";

        // Act
        var result = _service.Rewrite(query, 5);

        // Assert
        Assert.IsNotNull(result);

        if (result.Count > 1)
        {
            var uniqueResults = result.Distinct().ToList();
            Assert.AreEqual(result.Count, uniqueResults.Count, "Results should be unique");
        }

        foreach (var variant in result)
        {
            Console.WriteLine($"Unique variant: {variant}");
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Rewrite_WithLargeMaxCandidates_ShouldReturnReasonableAmount()
    {
        // Arrange
        var query = "房地产投资风险";
        var largeNumber = 20;

        // Act
        var result = _service.Rewrite(query, largeNumber);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count > 0);
        // Algorithm-based service should generate reasonable amount, not necessarily 20
        Assert.IsTrue(result.Count <= largeNumber);

        Console.WriteLine($"Generated {result.Count} variants for large request");
        foreach (var variant in result)
        {
            Console.WriteLine($"Generated variant: {variant}");
        }
    }

    #endregion
}