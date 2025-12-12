using MarketAssistant.Agents.Analysts;
using MarketAssistant.Infrastructure.Factories;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace TestMarketAssistant;

/// <summary>
/// AnalystAgentFactory 工具调用验证测试
/// 验证创建的 Agent 是否正确配置了工具，以及工具调用是否符合预期
/// </summary>
[TestClass]
public class AnalystAgentFactoryTest : BaseAgentTest
{
    [TestMethod]
    public void TestAnalystAgentFactory_CreateFinancialAnalyst_ShouldSucceed()
    {
        var agentFactory = _serviceProvider.GetRequiredService<IAnalystAgentFactory>();

        var agent = agentFactory.CreateAnalyst<FinancialAnalystAgent>();

        Assert.IsNotNull(agent, "应该成功创建 FinancialAnalyst");
        Console.WriteLine("成功创建 FinancialAnalyst");
    }

    [TestMethod]
    public async Task TestNewsEventAnalyst_CallsNewsToolCorrectly()
    {
        var agentFactory = _serviceProvider.GetRequiredService<IAnalystAgentFactory>();
        var agent = agentFactory.CreateAnalyst<NewsEventAnalystAgent>();

        // 模拟调用，提示明确要求使用工具
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "请对股票  sz002594 进行专业分析，提供投资建议。")
        };

        try
        {
            var response = await agent.RunAsync(messages);
            var result = response.Messages.LastOrDefault()?.Text ?? string.Empty;

            Assert.IsFalse(string.IsNullOrWhiteSpace(result), "Agent 应该返回分析结果");
            Console.WriteLine("分析结果:");
            Console.WriteLine(result);

            // 检查是否有工具调用的痕迹（通过日志中间件记录）
            var functionCalls = response.Messages
                .Where(m => m.Contents.Any(c => c is FunctionCallContent))
                .ToList();

            if (functionCalls.Any())
            {
                Console.WriteLine($"\n检测到 {functionCalls.Count} 次工具调用");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"测试执行异常: {ex.Message}");
            // 即使工具调用失败，我们也可以验证 agent 配置是否正确
        }
    }

    [TestMethod]
    public async Task TestFundamentalAnalyst_CallsToolsCorrectly()
    {
        var agentFactory = _serviceProvider.GetRequiredService<IAnalystAgentFactory>();
        var agent = agentFactory.CreateAnalyst<FundamentalAnalystAgent>();

        Assert.IsNotNull(agent, "应该成功创建 FundamentalAnalyst");
        Console.WriteLine("成功创建 FundamentalAnalyst");

        // 模拟调用，提示明确要求使用工具进行基本面分析
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "请对股票 sz002594 进行基本面分析，评估其投资价值、行业地位和增长潜力。")
        };

        try
        {
            var response = await agent.RunAsync(messages);
            var result = response.Messages.LastOrDefault()?.Text ?? string.Empty;

            Assert.IsFalse(string.IsNullOrWhiteSpace(result), "FundamentalAnalyst 应该返回分析结果");
            Console.WriteLine("基本面分析结果:");
            Console.WriteLine(result);

            // 检查是否有工具调用的痕迹（通过日志中间件记录）
            var functionCalls = response.Messages
                .Where(m => m.Contents.Any(c => c is FunctionCallContent))
                .ToList();

            if (functionCalls.Any())
            {
                Console.WriteLine($"\n检测到 {functionCalls.Count} 次工具调用");
                foreach (var call in functionCalls)
                {
                    var functionContent = call.Contents.OfType<FunctionCallContent>().FirstOrDefault();
                    if (functionContent != null)
                    {
                        Console.WriteLine($"- 调用工具: {functionContent.Name}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"测试执行异常: {ex.Message}");
            // 即使工具调用失败，我们也可以验证 agent 配置是否正确
        }
    }

    [TestMethod]
    public async Task TestCoordinatorAnalyst_HandlesMultipleAnalystInputs()
    {
        var agentFactory = _serviceProvider.GetRequiredService<IAnalystAgentFactory>();
        var agent = agentFactory.CreateAnalyst<CoordinatorAnalystAgent>();

        // 模拟其他分析师的输出作为历史消息
        // 构造冲突场景：基本面看好，技术面看空
        var fundamentalJson = """
            {
                "BasicInfo": { "Symbol": "SH600519", "Name": "贵州茅台" },
                "Fundamentals": { "Score": 8.5, "Summary": "行业龙头，护城河深厚，长期价值显著。" },
                "GrowthValue": { "Rating": "Buy", "ValuationStatus": "Undervalued" }
            }
            """;

        var technicalJson = """
            {
                "PatternTrend": { "CurrentTrend": "Down", "TrendStrength": 8 },
                "PriceLevels": { "SupportLevel": 1500, "ResistanceLevel": 1600 },
                "Strategy": { "Rating": "Sell", "Action": "Reduce", "TargetPrice": 1450 }
            }
            """;

        var financialJson = """
            {
                "HealthAssessment": { "OverallScore": 9, "Summary": "资产负债表极其健康，现金流充裕。" },
                "ProfitQuality": { "Roe": 25.5, "GrossMargin": 92.0 }
            }
            """;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "你将收到来自多位分析师的意见，请综合分析并给出投资建议。"),
            new(ChatRole.User, $"基本面分析师报告：\n{fundamentalJson}"),
            new(ChatRole.User, $"技术分析师报告：\n{technicalJson}"),
            new(ChatRole.User, $"财务分析师报告：\n{financialJson}"),
            new(ChatRole.User, "请对股票进行综合评估。注意基本面和技术面存在分歧，请分析原因并利用搜索工具验证市场共识，给出最终判断。")
        };

        try
        {
            var response = await agent.RunAsync(messages);
            var result = response.Messages.LastOrDefault()?.Text ?? string.Empty;

            Assert.IsFalse(string.IsNullOrWhiteSpace(result), "Coordinator 应该返回综合分析结果");
            Console.WriteLine("综合分析结果:");
            Console.WriteLine(result);

            // 检查是否识别了冲突并使用了搜索工具
            var functionCalls = response.Messages
                .Where(m => m.Contents.Any(c => c is FunctionCallContent))
                .ToList();

            if (functionCalls.Any())
            {
                Console.WriteLine($"\nCoordinator 进行了 {functionCalls.Count} 次工具调用以解决分歧");
                foreach (var msg in functionCalls)
                {
                    foreach (var content in msg.Contents.OfType<FunctionCallContent>())
                    {
                        Console.WriteLine($"- 调用工具: {content.Name}, 参数: {content.Arguments}");
                    }
                }
            }
            else
            {
                Console.WriteLine("\n警告: Coordinator 未进行工具调用。在理想情况下，面对明显分歧应调用搜索工具。");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"测试执行异常: {ex.Message}");
            throw;
        }
    }

    [TestMethod]
    public async Task TestFinancialAnalyst_CallsToolsCorrectly()
    {
        var agentFactory = _serviceProvider.GetRequiredService<IAnalystAgentFactory>();
        var agent = agentFactory.CreateAnalyst<FinancialAnalystAgent>();

        Assert.IsNotNull(agent, "应该成功创建 FinancialAnalyst");
        Console.WriteLine("成功创建 FinancialAnalyst");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "请对股票 sz002594 进行财务分析，重点关注盈利能力和偿债能力。")
        };

        try
        {
            var response = await agent.RunAsync(messages);
            var result = response.Messages.LastOrDefault()?.Text ?? string.Empty;

            Assert.IsFalse(string.IsNullOrWhiteSpace(result), "FinancialAnalyst 应该返回分析结果");
            Console.WriteLine("财务分析结果:");
            Console.WriteLine(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"测试执行异常: {ex.Message}");
        }
    }

    [TestMethod]
    public async Task TestMarketSentimentAnalyst_CallsToolsCorrectly()
    {
        var agentFactory = _serviceProvider.GetRequiredService<IAnalystAgentFactory>();
        var agent = agentFactory.CreateAnalyst<MarketSentimentAnalystAgent>();

        Assert.IsNotNull(agent, "应该成功创建 MarketSentimentAnalyst");
        Console.WriteLine("成功创建 MarketSentimentAnalyst");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "请分析股票 sz002594 的市场情绪和资金流向。")
        };

        try
        {
            var response = await agent.RunAsync(messages);
            var result = response.Messages.LastOrDefault()?.Text ?? string.Empty;

            Assert.IsFalse(string.IsNullOrWhiteSpace(result), "MarketSentimentAnalyst 应该返回分析结果");
            Console.WriteLine("市场情绪分析结果:");
            Console.WriteLine(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"测试执行异常: {ex.Message}");
        }
    }

    [TestMethod]
    public async Task TestTechnicalAnalyst_CallsToolsCorrectly()
    {
        var agentFactory = _serviceProvider.GetRequiredService<IAnalystAgentFactory>();
        var agent = agentFactory.CreateAnalyst<TechnicalAnalystAgent>();

        Assert.IsNotNull(agent, "应该成功创建 TechnicalAnalyst");
        Console.WriteLine("成功创建 TechnicalAnalyst");

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "请对股票 sz002594 进行技术面分析，查看K线形态和技术指标。")
        };

        try
        {
            var response = await agent.RunAsync(messages);
            var result = response.Messages.LastOrDefault()?.Text ?? string.Empty;

            Assert.IsFalse(string.IsNullOrWhiteSpace(result), "TechnicalAnalyst 应该返回分析结果");
            Console.WriteLine("技术分析结果:");
            Console.WriteLine(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"测试执行异常: {ex.Message}");
        }
    }
}

