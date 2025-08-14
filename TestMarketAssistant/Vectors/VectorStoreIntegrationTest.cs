using MarketAssistant.Vectors;
using MarketAssistant.Vectors.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Data;

namespace TestMarketAssistant.Vectors;

[TestClass]
public class VectorStoreIntegrationTest : BaseKernelTest
{
    private IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;
    private VectorStore? _vectorStore;

    [TestInitialize]
    public void VectorStoreIntegrationTestInitialize()
    {
        base.BaseInitialize();
        _embeddingGenerator = _kernel.Services.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
        _vectorStore = _kernel.Services.GetService<VectorStore>();
    }

    [TestMethod]
    public async Task VectorStore_ShouldStoreAndRetrieveTextParagraphs()
    {
        // Arrange
        Assert.IsNotNull(_vectorStore);
        Assert.IsNotNull(_embeddingGenerator);

        var collection = _vectorStore.GetCollection<string, TextParagraph>("testCollection");
        await collection.EnsureCollectionExistsAsync();

        var paragraphs = new[]
        {
            new TextParagraph
            {
                Key = "1",
                DocumentUri = "test://document1",
                ParagraphId = "1",
                Text = "This is the first test paragraph about artificial intelligence and machine learning."
            },
            new TextParagraph
            {
                Key = "2",
                DocumentUri = "test://document1",
                ParagraphId = "2",
                Text = "This is the second test paragraph about stock market trends and financial analysis."
            }
        };

        // Act - Store paragraphs
        foreach (var paragraph in paragraphs)
        {
            paragraph.TextEmbedding = await _embeddingGenerator.GenerateAsync(paragraph.Text);
            await collection.UpsertAsync(paragraph);
        }

        // Assert - Verify storage
        var storedParagraph = await collection.GetAsync("1");
        Assert.IsNotNull(storedParagraph);
        Assert.AreEqual("1", storedParagraph.Key);
        Assert.AreEqual("This is the first test paragraph about artificial intelligence and machine learning.", storedParagraph.Text);


        // Act - Search for similar paragraphs using vector search
        var searchVector = await _embeddingGenerator.GenerateAsync("first test paragraph");
        var vectorSearchResults = collection.SearchAsync(searchVector, 1);
        var vectorSearchResultsList = await vectorSearchResults.ToListAsync();

        // Assert - Verify vector search results
        Assert.IsNotNull(vectorSearchResultsList);
        Assert.AreEqual(1, vectorSearchResultsList.Count);

        // Act - Search for similar paragraphs
        var textSearch = new VectorStoreTextSearch<TextParagraph>(collection, _embeddingGenerator);
        var searchResults = await textSearch.GetTextSearchResultsAsync("artificial intelligence", new TextSearchOptions { Top = 1 });

        // Assert - Verify search results
        Assert.IsNotNull(searchResults);
        var resultsList = await searchResults.Results.ToListAsync();
        Assert.IsTrue(resultsList.Any());
    }

    [TestMethod]
    public async Task QueryRewriteService_ShouldGenerateRewrittenQueries()
    {
        // Arrange
        var service = new QueryRewriteService(_kernel);
        var query = "stock market analysis";

        // Act
        var result = await service.RewriteAsync(query);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count > 0);
        // Verify that none of the results are empty or whitespace
        foreach (var rewrittenQuery in result)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(rewrittenQuery));
        }
    }
}