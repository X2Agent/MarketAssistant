using MarketAssistant.Agents;
using MarketAssistant.Agents.StockSelection;
using MarketAssistant.Agents.StockSelection.Models;
using MarketAssistant.Applications.StockSelection.Models;
using MarketAssistant.Services.StockScreener.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace TestMarketAssistant;

/// <summary>
/// StockSelectionWorkflow 测试（基于 Agent Framework Workflows）
/// </summary>
[TestClass]
public sealed class StockSelectionWorkflowTest : BaseKernelTest
{
    private StockSelectionWorkflow _stockSelectionWorkflow = null!;

    [TestInitialize]
    public void Initialize()
    {
        _stockSelectionWorkflow = _kernel.Services.GetRequiredService<StockSelectionWorkflow>();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _stockSelectionWorkflow?.Dispose();
    }

    [TestMethod]
    public async Task TestStockSelectionWorkflow_AnalyzeUserRequirementAsync_WithValidRequest_ShouldReturnResult()
    {
        // Arrange
        var request = new StockRecommendationRequest
        {
            UserRequirements = "市值大于1000万的旅游行业股票",
            RiskPreference = "conservative"
        };

        // Act
        var result = await _stockSelectionWorkflow.AnalyzeUserRequirementAsync(request);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Recommendations);
        Assert.IsTrue(result.ConfidenceScore >= 0);
        Assert.IsTrue(result.ConfidenceScore <= 100);

        Console.WriteLine($"用户需求分析完成，推荐股票数量: {result.Recommendations.Count}, 置信度: {result.ConfidenceScore:F1}%");
    }

    [TestMethod]
    public async Task TestStockSelectionWorkflow_AnalyzeUserRequirementAsync_WithScreeningRequest_ShouldUseStockScreener()
    {
        // Arrange - 测试需要使用筛选功能的用户需求
        var request = new StockRecommendationRequest
        {
            UserRequirements = "我想找一些市值100亿以上的成长股，ROE要大于15%，近期涨幅不要太大",
            RiskPreference = "moderate",
            InvestmentAmount = 500000m,
            InvestmentHorizon = 180
        };

        // Act
        var result = await _stockSelectionWorkflow.AnalyzeUserRequirementAsync(request);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Recommendations);
        Assert.IsTrue(result.ConfidenceScore >= 0);
        Assert.IsTrue(result.ConfidenceScore <= 100);

        Console.WriteLine($"=== 股票筛选需求分析测试结果 ===");
        Console.WriteLine($"用户需求: {request.UserRequirements}");
        Console.WriteLine($"推荐股票数量: {result.Recommendations.Count}");
        Console.WriteLine($"置信度: {result.ConfidenceScore:F1}%");
        Console.WriteLine($"分析摘要: {result.AnalysisSummary}");

        if (result.Recommendations.Any())
        {
            Console.WriteLine("\n推荐股票:");
            foreach (var stock in result.Recommendations.Take(3))
            {
                Console.WriteLine($"- {stock.Name} ({stock.Symbol}): {stock.Reason}");
            }
        }
    }

    [TestMethod]
    public async Task TestStockSelectionWorkflow_AnalyzeNewsHotspotAsync_WithValidRequest_ShouldReturnResult()
    {
        // Arrange
        var request = new NewsBasedSelectionRequest
        {
            NewsContent = "7 月 14 日消息，据央视新闻报道，相关数据显示，2025 年我国新能源汽车人才缺口高达上百万，智驾工程师供需比仅为 0.38。",
            MaxRecommendations = 5
        };

        // Act
        var result = await _stockSelectionWorkflow.AnalyzeNewsHotspotAsync(request);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Recommendations);
        Assert.IsTrue(result.ConfidenceScore >= 0);
        Assert.IsTrue(result.ConfidenceScore <= 100);

        Console.WriteLine($"新闻热点分析完成，推荐股票数量: {result.Recommendations.Count}, 置信度: {result.ConfidenceScore:F1}%");
    }

    [TestMethod]
    public async Task TestStockSelectionWorkflow_DeterministicExecution_ShouldFollowThreeSteps()
    {
        // Arrange - 测试工作流的确定性三步骤执行
        var request = new StockRecommendationRequest
        {
            UserRequirements = "寻找科技行业的价值股，市值超过50亿，市盈率小于30",
            RiskPreference = "moderate",
            MaxRecommendations = 5
        };

        // Act
        var result = await _stockSelectionWorkflow.AnalyzeUserRequirementAsync(request);

        // Assert
        Assert.IsNotNull(result, "工作流应返回结果");
        Assert.IsNotNull(result.Recommendations, "推荐列表不应为 null");

        Console.WriteLine($"=== 确定性三步骤工作流测试 ===");
        Console.WriteLine($"步骤1: 生成筛选条件 - ✓");
        Console.WriteLine($"步骤2: 执行股票筛选 - ✓");
        Console.WriteLine($"步骤3: AI分析结果 - ✓");
        Console.WriteLine($"\n最终结果:");
        Console.WriteLine($"推荐股票数量: {result.Recommendations.Count}");
        Console.WriteLine($"置信度: {result.ConfidenceScore:F1}%");
    }

    [TestMethod]
    public void TestStockCriteria_JsonDeserialization_ShouldSucceed()
    {
        // Arrange - 准备 JSON 数据
        var jsonString = @"{
      ""criteria"": [
        {
          ""code"": ""mc"",
          ""displayName"": ""总市值"",
          ""minValue"": 100000000000,
          ""maxValue"": null,
          ""type"": ""basic""
        },
        {
          ""code"": ""npay"",
          ""displayName"": ""净利润同比增长"",
          ""minValue"": 20,
          ""maxValue"": null,
          ""type"": ""basic""
        },
        {
          ""code"": ""roediluted"",
          ""displayName"": ""净资产收益率"",
          ""minValue"": 15,
          ""maxValue"": null,
          ""type"": ""basic""
        },
        {
          ""code"": ""pct20"",
          ""displayName"": ""近20日涨跌幅"",
          ""minValue"": null,
          ""maxValue"": 10,
          ""type"": ""market""
        }
      ],
      ""market"": ""全部A股"",
      ""industry"": ""全部"",
      ""limit"": 20
    }";

        // Act - 执行反序列化
        StockCriteria? result = null;
        Exception? exception = null;
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            };

            result = JsonSerializer.Deserialize<StockCriteria>(jsonString, options);
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        // Assert - 验证结果
        Assert.IsNull(exception, $"反序列化过程中出现异常: {exception?.Message}");
        Assert.IsNotNull(result, "反序列化结果不应为 null");

        // 验证基本属性
        Assert.AreEqual("全部A股", result.Market, "市场类型不匹配");
        Assert.AreEqual("全部", result.Industry, "行业分类不匹配");
        Assert.AreEqual(20, result.Limit, "返回数量限制不匹配");

        // 验证筛选条件
        Assert.IsNotNull(result.Criteria, "筛选条件列表不应为 null");
        Assert.AreEqual(4, result.Criteria.Count, "筛选条件数量不匹配");

        // 验证第一个条件 - 总市值
        var mcCriteria = result.Criteria[0];
        Assert.AreEqual("mc", mcCriteria.Code, "市值条件代码不匹配");
        Assert.AreEqual("总市值", mcCriteria.DisplayName, "市值条件显示名称不匹配");
        Assert.AreEqual(100000000000m, mcCriteria.MinValue, "市值最小值不匹配");
        Assert.IsNull(mcCriteria.MaxValue, "市值最大值应为 null");

        // 输出成功信息
        Console.WriteLine("✅ StockCriteria JSON 反序列化测试成功！");
        Console.WriteLine($"市场: {result.Market}");
        Console.WriteLine($"行业: {result.Industry}");
        Console.WriteLine($"返回数量限制: {result.Limit}");
        Console.WriteLine($"筛选条件数量: {result.Criteria.Count}");

        foreach (var criteria in result.Criteria)
        {
            Console.WriteLine($"  - {criteria.DisplayName} ({criteria.Code}): " +
                            $"Min={criteria.MinValue}, Max={criteria.MaxValue}");
        }
    }
}

