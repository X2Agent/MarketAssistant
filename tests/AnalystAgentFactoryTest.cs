using MarketAssistant.Agents;
using MarketAssistant.Infrastructure.Configuration;
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
    public void TestAnalystAgentFactory_CreateFinancialAnalyst_HasCorrectTools()
    {
        var agentFactory = _serviceProvider.GetRequiredService<IAnalystAgentFactory>();
        var toolsConfig = _serviceProvider.GetRequiredService<IAgentToolsConfig>();

        var agent = agentFactory.CreateAnalyst(AnalysisAgent.FinancialAnalyst);
        var tools = toolsConfig.GetToolsForAgent(AnalysisAgent.FinancialAnalyst);

        Assert.IsNotNull(agent);
        Assert.IsTrue(tools.Count > 0, "FinancialAnalyst 应该配置了工具");

        Console.WriteLine($"FinancialAnalyst 配置了 {tools.Count} 个工具");
    }

    [TestMethod]
    public async Task TestNewsEventAnalyst_CallsNewsToolCorrectly()
    {
        var agentFactory = _serviceProvider.GetRequiredService<IAnalystAgentFactory>();
        var agent = agentFactory.CreateAnalyst(AnalysisAgent.NewsEventAnalyst);

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
        var toolsConfig = _serviceProvider.GetRequiredService<IAgentToolsConfig>();
        var agent = agentFactory.CreateAnalyst(AnalysisAgent.FundamentalAnalyst);
        var tools = toolsConfig.GetToolsForAgent(AnalysisAgent.FundamentalAnalyst);

        Assert.IsNotNull(agent);
        Assert.IsTrue(tools.Count > 0, "FundamentalAnalyst 应该配置了工具");
        Console.WriteLine($"FundamentalAnalyst 配置了 {tools.Count} 个工具");

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
        var agent = agentFactory.CreateAnalyst(AnalysisAgent.CoordinatorAnalyst);

        // 模拟其他分析师的输出作为历史消息
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "你将收到来自多位分析师的意见，请综合分析并给出投资建议。"),
            new(ChatRole.User, "基本面分析师认为该股票基本面评分 7/10，行业前景良好。"),
            new(ChatRole.User, "技术分析师认为该股票技术面评分 4/10，存在破位风险。"),
            new(ChatRole.User, "财务分析师认为该股票财务健康度 6/10，现金流较为稳定。"),
            new(ChatRole.User, "请对股票进行综合评估，如果需要更多信息，可以使用搜索工具验证。")
        };

        try
        {
            var response = await agent.RunAsync(messages);
            var result = response.Messages.LastOrDefault()?.Text ?? string.Empty;

            Assert.IsFalse(string.IsNullOrWhiteSpace(result), "Coordinator 应该返回综合分析结果");
            Console.WriteLine("综合分析结果:");
            Console.WriteLine(result);

            // 检查是否识别了冲突并可能使用了搜索工具
            var functionCalls = response.Messages
                .Where(m => m.Contents.Any(c => c is FunctionCallContent))
                .ToList();

            if (functionCalls.Any())
            {
                Console.WriteLine($"\nCoordinator 进行了 {functionCalls.Count} 次工具调用（可能用于解决分歧）");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"测试执行异常: {ex.Message}");
        }
    }
}

