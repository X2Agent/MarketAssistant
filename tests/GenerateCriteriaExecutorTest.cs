using MarketAssistant.Agents.StockSelection.Executors;
using MarketAssistant.Agents.StockSelection.Models;
using MarketAssistant.Services.StockScreener.Models;
using Microsoft.Extensions.DependencyInjection;

namespace TestMarketAssistant;

/// <summary>
/// GenerateCriteriaExecutor 单元测试
/// </summary>
[TestClass]
public sealed class GenerateCriteriaExecutorTest : BaseAgentTest
{
    private GenerateCriteriaExecutor _executor = null!;

    [TestInitialize]
    public void Initialize()
    {
        _executor = _serviceProvider.GetRequiredService<GenerateCriteriaExecutor>();
    }

    [TestMethod]
    public async Task GenerateCriteria_NoIndustryMentioned_ShouldReturnAll()
    {
        // Arrange
        var request = new StockSelectionWorkflowRequest
        {
            Content = "寻找市值大于100亿的成长股，ROE要大于15%",
            MaxRecommendations = 20,
            IsNewsAnalysis = false
        };

        // Act
        var result = await _executor.HandleAsync(request, null!, CancellationToken.None);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Criteria);

        // 关键断言：没有提到行业时，应该是 All（全部）
        Console.WriteLine($"解析的行业: {result.Criteria.Industry}");
        Console.WriteLine($"行业枚举值: {(int)result.Criteria.Industry}");
        Console.WriteLine($"筛选条件数量: {result.Criteria.Criteria?.Count ?? 0}");

        foreach (var criterion in result.Criteria.Criteria ?? new())
        {
            Console.WriteLine($"  - {criterion.DisplayName}: {criterion.MinValue} ~ {criterion.MaxValue}");
        }

        Assert.AreEqual(IndustryType.All, result.Criteria.Industry,
            $"用户需求中没有提到行业，应该解析为 All，但实际解析为 {result.Criteria.Industry}");
    }

    [TestMethod]
    public async Task GenerateCriteria_TechnologyIndustry_ShouldReturnCorrectIndustry()
    {
        // Arrange
        var request = new StockSelectionWorkflowRequest
        {
            Content = "从沪A中，寻找市值大于100亿的科技股，专注软件开发领域，ROE要大于15%",
            MaxRecommendations = 20,
            IsNewsAnalysis = false
        };

        // Act
        var result = await _executor.HandleAsync(request, null!, CancellationToken.None);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Criteria);

        Console.WriteLine($"解析的行业: {result.Criteria.Industry}");

        // 应该解析为软件开发或计算机设备
        Assert.IsTrue(
            result.Criteria.Industry == IndustryType.SoftwareDevelopment ||
            result.Criteria.Industry == IndustryType.ComputerEquipment,
            $"应该解析为软件开发或计算机设备，但实际解析为 {result.Criteria.Industry}");
    }
}
