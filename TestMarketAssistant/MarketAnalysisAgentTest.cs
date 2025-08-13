using MarketAssistant.Agents;
using MarketAssistant.Infrastructure;
using Microsoft.Extensions.AI;
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
        var embeddingGenerator = _kernel.Services.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
        var vectorStore = _kernel.Services.GetService<VectorStore>();
        var kernelPluginConfig = _kernel.Services.GetService<IKernelPluginConfig>();
        var analystManager = new AnalystManager(_kernel, analystManagerLogger, _userSettingService, embeddingGenerator, vectorStore, kernelPluginConfig);
        _marketAnalysisAgent = new MarketAnalysisAgent(logger, analystManager);
    }

    [TestMethod]
    public async Task TestAnalysisAsync()
    {
        var result = await _marketAnalysisAgent.AnalysisAsync("sh601606");
        Assert.IsNotNull(result);
    }
}
