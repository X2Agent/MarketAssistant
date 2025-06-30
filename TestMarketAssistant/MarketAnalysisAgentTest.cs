using MarketAssistant.Agents;
using MarketAssistant.Infrastructure;
using Microsoft.SemanticKernel;

namespace TestMarketAssistant;

[TestClass]
public class MarketAnalysisAgentTest : BaseKernelTest
{
    private MarketAnalysisAgent _marketAnalysisAgent = null!;
    private Kernel _kernel = null!;

    [TestInitialize]
    public void Initialize()
    {
        _kernel = CreateKernelWithChatCompletion();
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<MarketAnalysisAgent>();
        var analystManagerLogger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<AnalystManager>();
        var userSettingService = _kernel.Services.GetService<IUserSettingService>();
        var analystManager = new AnalystManager(_kernel, analystManagerLogger, userSettingService);
        _marketAnalysisAgent = new MarketAnalysisAgent(logger, analystManager);
    }

    [TestMethod]
    public async Task TestAnalysisAsync()
    {
        var result = await _marketAnalysisAgent.AnalysisAsync("sz002594");
        Assert.IsNotNull(result);
    }
}
