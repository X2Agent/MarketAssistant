using MarketAssistant.Vectors;
using MarketAssistant.Vectors.Interfaces;
using MarketAssistant.Vectors.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using Moq;
using System.Collections.Generic;

namespace TestMarketAssistant.Vectors;

[TestClass]
public class RagIngestionServiceTest
{
    [TestMethod]
    public async Task IngestFileAsync_ShouldCallServicesInCorrectOrder()
    {
        // Arrange
        var cleaningServiceMock = new Mock<ITextCleaningService>();
        var chunkingServiceMock = new Mock<ITextChunkingService>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        var loggerMock = new Mock<ILogger<RagIngestionService>>();

        var rawDocumentReaderMock = new Mock<IRawDocumentReader>();
        rawDocumentReaderMock.Setup(x => x.ReadAllText(It.IsAny<Stream>())).Returns("raw text");

        serviceProviderMock.Setup(x => x.GetKeyedService<IRawDocumentReader>(It.IsAny<string>()))
                          .Returns(rawDocumentReaderMock.Object);

        cleaningServiceMock.Setup(x => x.Clean(It.IsAny<string>())).Returns("cleaned text");

        var textParagraphs = new[] {
            new TextParagraph { Key = "1", DocumentUri = "test://document", ParagraphId = "1", Text = "paragraph 1" },
            new TextParagraph { Key = "2", DocumentUri = "test://document", ParagraphId = "2", Text = "paragraph 2" }
        };

        chunkingServiceMock.Setup(x => x.Chunk(It.IsAny<string>(), It.IsAny<string>()))
                          .Returns(textParagraphs);

        var collectionMock = new Mock<VectorStoreCollection<string, TextParagraph>>();
        var embeddingGeneratorMock = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();

        var embeddingMock = new Mock<Embedding<float>>();
        embeddingGeneratorMock.Setup(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<EmbeddingGenerationOptions?>(), It.IsAny<CancellationToken>()))
                              .ReturnsAsync(embeddingMock.Object);

        var service = new RagIngestionService(
            cleaningServiceMock.Object,
            chunkingServiceMock.Object,
            serviceProviderMock.Object,
            loggerMock.Object);

        var filePath = "test.txt";

        // Act
        await service.IngestFileAsync(collectionMock.Object, filePath, embeddingGeneratorMock.Object);

        // Assert
        rawDocumentReaderMock.Verify(x => x.ReadAllText(It.IsAny<Stream>()), Times.Once);
        cleaningServiceMock.Verify(x => x.Clean("raw text"), Times.Once);
        chunkingServiceMock.Verify(x => x.Chunk(filePath, "cleaned text"), Times.Once);
        embeddingGeneratorMock.Verify(x => x.GenerateAsync(It.IsAny<string>(), It.IsAny<EmbeddingGenerationOptions?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        collectionMock.Verify(x => x.UpsertAsync(It.IsAny<TextParagraph>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [TestMethod]
    public async Task IngestFileAsync_ShouldHandleMissingDocumentReader()
    {
        // Arrange
        var cleaningServiceMock = new Mock<ITextCleaningService>();
        var chunkingServiceMock = new Mock<ITextChunkingService>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        var loggerMock = new Mock<ILogger<RagIngestionService>>();

        // Return null for any document reader
        serviceProviderMock.Setup(x => x.GetKeyedService<IRawDocumentReader>(It.IsAny<string>()))
                          .Returns((IRawDocumentReader?)null);

        var collectionMock = new Mock<VectorStoreCollection<string, TextParagraph>>();
        var embeddingGeneratorMock = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();

        var service = new RagIngestionService(
            cleaningServiceMock.Object,
            chunkingServiceMock.Object,
            serviceProviderMock.Object,
            loggerMock.Object);

        var filePath = "test.txt";

        // Act
        await service.IngestFileAsync(collectionMock.Object, filePath, embeddingGeneratorMock.Object);

        // Assert
        // Should not throw an exception and should not call any other services
        cleaningServiceMock.Verify(x => x.Clean(It.IsAny<string>()), Times.Never);
    }
}