using MarketAssistant.Agents;
using MarketAssistant.Agents.MarketAnalysis;
using MarketAssistant.Infrastructure.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;

namespace TestMarketAssistant;

[TestClass]
public class MarketAnalysisAgentTest : BaseKernelTest
{
    private MarketAnalysisAgent _marketAnalysisAgent = null!;

    [TestInitialize]
    public void Initialize()
    {
        BaseInitialize();
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<MarketAnalysisAgent>();
        var marketAnalysisWorkflow = _kernel.Services.GetRequiredService<MarketAnalysisWorkflow>();
        _marketAnalysisAgent = new MarketAnalysisAgent(logger, marketAnalysisWorkflow);
    }

    [TestMethod]
    public async Task TestAnalysisAsync()
    {
        var result = await _marketAnalysisAgent.AnalysisAsync("sh601606");
        Assert.IsNotNull(result);
    }
}
