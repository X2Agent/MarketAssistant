using MarketAssistant.Agents;
using MarketAssistant.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
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
        var embeddingGenerator = _kernel.Services.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
        var vectorStore = _kernel.Services.GetService<VectorStore>();
        var analystManager = new AnalystManager(_kernel, analystManagerLogger, userSettingService, embeddingGenerator, vectorStore);
        _marketAnalysisAgent = new MarketAnalysisAgent(logger, analystManager);
    }

    [TestMethod]
    public async Task TestAnalysisAsync()
    {
        var result = await _marketAnalysisAgent.AnalysisAsync("sh601606");
        Assert.IsNotNull(result);
    }
}
