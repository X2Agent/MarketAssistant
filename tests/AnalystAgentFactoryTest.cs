using MarketAssistant.Agents.Analysts;
using MarketAssistant.Infrastructure.Factories;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace TestMarketAssistant;

/// <summary>
/// AnalystAgentFactory 工具调用验证测试
/// 使用 MAF Evaluation 框架验证 Agent 工具调用与响应质量
/// 需要 OPENAI_API_KEY 环境变量；缺失时自动跳过
/// </summary>
[TestClass]
public class AnalystAgentFactoryTest : BaseAgentTest
{
    private const string StockSymbol = "sz002594";

    [TestMethod]
    [TestCategory("Agent")]
    public void TestAnalystAgentFactory_CreateFinancialAnalyst_ShouldSucceed()
    {
        RequireLlm();
        var agentFactory = _serviceProvider.GetRequiredService<IAnalystAgentFactory>();
        var agent = agentFactory.CreateAnalyst(typeof(FinancialAnalystAgent));

        Assert.IsNotNull(agent, "应该成功创建 FinancialAnalyst");
    }

    [TestMethod]
    [TestCategory("Agent")]
    public async Task TestNewsEventAnalyst_CallsNewsToolCorrectly()
    {
        RequireLlm();

        var agent = _analystAgentFactory.CreateAnalyst(typeof(NewsEventAnalystAgent));
        Assert.IsNotNull(agent);

        var evaluator = CreateAnalystEvaluator(
            EvalChecks.ToolCalledCheck(ToolCalledMode.Any, "GetNewsAsync"),
            MeaningfulJsonResponseCheck());

        var results = await agent.EvaluateAsync(
            ["请对股票 sz002594 进行专业分析，提供投资建议。"],
            evaluator);

        results.AssertAllPassed();
    }

    [TestMethod]
    [TestCategory("Agent")]
    public async Task TestFundamentalAnalyst_CallsToolsCorrectly()
    {
        RequireLlm();

        var agent = _analystAgentFactory.CreateAnalyst(typeof(FundamentalAnalystAgent));
        Assert.IsNotNull(agent);

        var evaluator = CreateAnalystEvaluator(
            EvalChecks.ToolCalledCheck(ToolCalledMode.Any, "GetAssetInfoAsync", "GetCompanyInfoAsync"),
            MeaningfulJsonResponseCheck());

        var results = await agent.EvaluateAsync(
            [$"请对股票 {StockSymbol} 进行基本面分析，评估其投资价值、行业地位和增长潜力。"],
            evaluator);

        results.AssertAllPassed();
    }

    [TestMethod]
    [TestCategory("Agent")]
    public async Task TestCoordinatorAnalyst_HandlesMultipleAnalystInputs()
    {
        RequireLlm();

        var agent = _analystAgentFactory.CreateAnalyst(typeof(CoordinatorAnalystAgent));
        Assert.IsNotNull(agent);

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

        const string finalQuery = "请对股票进行综合评估。注意基本面和技术面存在分歧，请分析原因并利用搜索工具验证市场共识，给出最终判断。";

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "你将收到来自多位分析师的意见，请综合分析并给出投资建议。"),
            new(ChatRole.User, $"基本面分析师报告：\n{fundamentalJson}"),
            new(ChatRole.User, $"技术分析师报告：\n{technicalJson}"),
            new(ChatRole.User, $"财务分析师报告：\n{financialJson}"),
            new(ChatRole.User, finalQuery)
        };

        var response = await agent.RunAsync(messages);
        AssertFunctionCallsPresent(response.Messages);

        var evaluator = CreateAnalystEvaluator(
            EvalChecks.ToolCalledCheck(ToolCalledMode.Any, "SearchAsync"),
            EvalChecks.NonEmpty(100),
            EvalChecks.KeywordCheck("分歧", "综合"),
            FunctionEvaluator.Create(
                "MeaningfulCoordinatorResponse",
                responseText => !string.IsNullOrWhiteSpace(responseText) && responseText.Length >= 100));

        var results = await agent.EvaluateAsync([response], [finalQuery], evaluator);
        results.AssertAllPassed();
    }

    [TestMethod]
    [TestCategory("Agent")]
    public async Task TestFinancialAnalyst_CallsToolsCorrectly()
    {
        RequireLlm();

        var agent = _analystAgentFactory.CreateAnalyst(typeof(FinancialAnalystAgent));
        Assert.IsNotNull(agent);

        var evaluator = CreateAnalystEvaluator(
            EvalChecks.ToolCalledCheck(
                ToolCalledMode.Any,
                "GetBalanceSheetAsync",
                "GetIncomeStatementAsync",
                "GetCashFlowStatementAsync",
                "GetFinancialRatiosAsync",
                "GetCapitalStructureAsync"),
            MeaningfulJsonResponseCheck());

        var results = await agent.EvaluateAsync(
            [$"请对股票 {StockSymbol} 进行财务分析，重点关注盈利能力和偿债能力。"],
            evaluator);

        results.AssertAllPassed();
    }

    [TestMethod]
    [TestCategory("Agent")]
    public async Task TestMarketSentimentAnalyst_CallsToolsCorrectly()
    {
        RequireLlm();

        var agent = _analystAgentFactory.CreateAnalyst(typeof(MarketSentimentAnalystAgent));
        Assert.IsNotNull(agent);

        var evaluator = CreateAnalystEvaluator(
            EvalChecks.ToolCalledCheck(
                ToolCalledMode.Any,
                "GetFundFlowAsync",
                "GetBalanceSheetAsync",
                "GetIncomeStatementAsync",
                "GetFinancialRatiosAsync"),
            MeaningfulJsonResponseCheck());

        var results = await agent.EvaluateAsync(
            [$"请分析股票 {StockSymbol} 的市场情绪和资金流向。"],
            evaluator);

        results.AssertAllPassed();
    }

    [TestMethod]
    [TestCategory("Agent")]
    public async Task TestTechnicalAnalyst_CallsToolsCorrectly()
    {
        RequireLlm();

        var agent = _analystAgentFactory.CreateAnalyst(typeof(TechnicalAnalystAgent));
        Assert.IsNotNull(agent);

        var evaluator = CreateAnalystEvaluator(
            EvalChecks.ToolCalledCheck(
                ToolCalledMode.Any,
                "GetKDJAsync",
                "GetMACDAsync",
                "GetBOLLAsync",
                "GetMAAsync",
                "GetKLinesAsync"),
            MeaningfulJsonResponseCheck());

        var results = await agent.EvaluateAsync(
            [$"请对股票 {StockSymbol} 进行技术面分析，查看K线形态和技术指标。"],
            evaluator);

        results.AssertAllPassed();
    }

    private static LocalEvaluator CreateAnalystEvaluator(params EvalCheck[] additionalChecks)
    {
        var checks = new List<EvalCheck>
        {
            EvalChecks.ToolCallsPresent(),
            EvalChecks.NonEmpty(50),
            FunctionEvaluator.Create(
                "HasFunctionCallContent",
                item => item.Conversation?.Any(m => m.Contents.Any(c => c is FunctionCallContent)) == true)
        };
        checks.AddRange(additionalChecks);
        return new LocalEvaluator(checks.ToArray());
    }

    private static EvalCheck MeaningfulJsonResponseCheck() =>
        FunctionEvaluator.Create(
            "MeaningfulJsonResponse",
            response => !string.IsNullOrWhiteSpace(response)
                && response.TrimStart().StartsWith('{')
                && response.Length >= 50);

    private static void AssertFunctionCallsPresent(IEnumerable<ChatMessage> messages)
    {
        Assert.IsTrue(
            messages.Any(m => m.Contents.Any(c => c is FunctionCallContent)),
            "Agent 响应应包含工具调用");
    }
}
