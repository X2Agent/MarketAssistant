using MarketAssistant.Agents;
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
        var analystManagerLogger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<AnalystManager>();
        var kernelPluginConfig = _kernel.Services.GetRequiredService<IKernelPluginConfig>();
        var analystManager = new AnalystManager(_kernel, analystManagerLogger, _userSettingService, kernelPluginConfig);
        _marketAnalysisAgent = new MarketAnalysisAgent(logger, analystManager);
    }

    [TestMethod]
    public async Task TestAnalysisAsync()
    {
        var result = await _marketAnalysisAgent.AnalysisAsync("sh601606");
        Assert.IsNotNull(result);
    }
}
