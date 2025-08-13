using MarketAssistant.Vectors.Services;
using Microsoft.SemanticKernel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace TestMarketAssistant.Vectors;

[TestClass]
public class QueryRewriteServiceTest
{
    [TestMethod]
    public async Task RewriteAsync_ShouldReturnRewrittenQueries()
    {
        // Arrange
        var kernelMock = new Mock<Kernel>();
        var kernelResultMock = new Mock<FunctionResult>(typeof(string));
        kernelResultMock.Setup(x => x.GetValue<string>()).Returns("rewritten query 1\nrewritten query 2\nrewritten query 3");
        
        kernelMock.Setup(x => x.InvokePromptAsync(It.IsAny<string>(), It.IsAny<KernelArguments?>(), It.IsAny<string?>(), It.IsAny<IPromptTemplateFactory?>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(kernelResultMock.Object);
        
        var service = new QueryRewriteService(kernelMock.Object);
        var query = "original query";

        // Act
        var result = await service.RewriteAsync(query);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(3, result.Count);
        Assert.AreEqual("rewritten query 1", result[0]);
        Assert.AreEqual("rewritten query 2", result[1]);
        Assert.AreEqual("rewritten query 3", result[2]);
    }

    [TestMethod]
    public async Task RewriteAsync_ShouldHandleEmptyQuery()
    {
        // Arrange
        var kernelMock = new Mock<Kernel>();
        var service = new QueryRewriteService(kernelMock.Object);
        var query = "";

        // Act
        var result = await service.RewriteAsync(query);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task RewriteAsync_ShouldHandleNullQuery()
    {
        // Arrange
        var kernelMock = new Mock<Kernel>();
        var service = new QueryRewriteService(kernelMock.Object);
        string? query = null;

        // Act
        var result = await service.RewriteAsync(query!);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task RewriteAsync_ShouldLimitResultsToMaxCandidates()
    {
        // Arrange
        var kernelMock = new Mock<Kernel>();
        var kernelResultMock = new Mock<FunctionResult>(typeof(string));
        kernelResultMock.Setup(x => x.GetValue<string>()).Returns("rewritten query 1\nrewritten query 2\nrewritten query 3\nrewritten query 4\nrewritten query 5");
        
        kernelMock.Setup(x => x.InvokePromptAsync(It.IsAny<string>(), It.IsAny<KernelArguments?>(), It.IsAny<string?>(), It.IsAny<IPromptTemplateFactory?>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(kernelResultMock.Object);
        
        var service = new QueryRewriteService(kernelMock.Object);
        var query = "original query";
        var maxCandidates = 3;

        // Act
        var result = await service.RewriteAsync(query, maxCandidates);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(maxCandidates, result.Count);
    }
}