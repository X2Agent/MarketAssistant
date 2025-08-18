using MarketAssistant.Agents;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Moq;

namespace TestMarketAssistant;

[TestClass]
public class MarketAnalysisAgentTest : BaseKernelTest
{
    private MarketAnalysisAgent _marketAnalysisAgent = null!;

    [TestInitialize]
    public void Initialize()
    {
        BaseInitialize(); // 调用基类初始化方法
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<MarketAnalysisAgent>();
        var analystManagerLogger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<AnalystManager>();
        var embeddingGenerator = _kernel.Services.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
        var vectorStore = _kernel.Services.GetService<VectorStore>();
        var mockKernelPluginConfig = new Mock<MarketAssistant.Infrastructure.IKernelPluginConfig>();
        var analystManager = new AnalystManager(_kernel, analystManagerLogger, _userSettingService, embeddingGenerator, vectorStore, mockKernelPluginConfig.Object);
        _marketAnalysisAgent = new MarketAnalysisAgent(logger, analystManager);
    }

    [TestMethod]
    public async Task TestAnalysisAsync()
    {
        var result = await _marketAnalysisAgent.AnalysisAsync("sh601606");
        Assert.IsNotNull(result);
    }
}
