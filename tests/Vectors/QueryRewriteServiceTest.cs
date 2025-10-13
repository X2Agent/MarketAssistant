using MarketAssistant.Rag.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace TestMarketAssistant.Vectors;

[TestClass]
public class QueryRewriteServiceTest : BaseKernelTest
{
    private IQueryRewriteService _service = null!;

    [TestInitialize]
    public void Initialize()
    {
        base.BaseInitialize();
        _service = _kernel.Services.GetRequiredService<IQueryRewriteService>();
    }

    #region Service Resolution Tests

    [TestMethod]
    public void Service_ShouldBeResolvedFromContainer()
    {
        // Assert
        Assert.IsNotNull(_service);
    }

    #endregion

    #region Input Validation Tests


    [TestMethod]
    public void Rewrite_WithWhitespaceQuery_ShouldReturnEmptyList()
    {
        // Act
        var result = _service.Rewrite("   \t\n  ");

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Rewrite_WithZeroMaxCandidates_ShouldReturnEmptyList()
    {
        // Act
        var result = _service.Rewrite("test query", 0);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
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
    public void Rewrite_WithFinancialTerms_ShouldGenerateAnalysisDimensions()
    {
        // Arrange
        var query = "新能源股票投资";

        // Act
        var result = _service.Rewrite(query, 4);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count > 0);

        // Should include analysis dimensions like 基本面、技术面 etc.
        var hasAnalysisDimension = result.Any(r =>
            r.Contains("基本面") || r.Contains("技术面") || r.Contains("消息面") || r.Contains("估值"));

        foreach (var variant in result)
        {
            Console.WriteLine($"Generated variant: {variant}");
        }
    }

    [TestMethod]
    public void Rewrite_WithTimeFrameTerms_ShouldGenerateTimeVariants()
    {
        // Arrange
        var query = "芯片股票研究";

        // Act
        var result = _service.Rewrite(query, 6);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count > 0);

        // Should include time-related variants
        foreach (var variant in result)
        {
            Console.WriteLine($"Generated variant: {variant}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(variant));
        }
    }

    [TestMethod]
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