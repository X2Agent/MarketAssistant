using MarketAssistant.Rag;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;

namespace TestMarketAssistant.Vectors;

[TestClass]
public class VectorStoreIntegrationTest : BaseAgentTest
{
    private IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;
    private VectorStore? _vectorStore;

    [TestInitialize]
    public void VectorStoreIntegrationTestInitialize()
    {
        base.BaseInitialize();
        _embeddingGenerator = _serviceProvider.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
        _vectorStore = _serviceProvider.GetService<VectorStore>();
    }

    [TestMethod]
    public async Task VectorStore_ShouldStoreAndRetrieveTextParagraphs()
    {
        // Arrange
        Assert.IsNotNull(_vectorStore);
        Assert.IsNotNull(_embeddingGenerator);

        // Use a unique collection name for this test to avoid schema conflicts
        var collectionName = $"testCollection_{Guid.NewGuid():N}";
        var collection = _vectorStore.GetCollection<string, TextParagraph>(collectionName);
        await collection.EnsureCollectionExistsAsync();

        var paragraphs = new[]
        {
            new TextParagraph
            {
                Key = "1",
                DocumentUri = "test://document1",
                ParagraphId = "1",
                Text = "This is the first test paragraph about artificial intelligence and machine learning.",
                Order = 0,
                Section = "Introduction",
                SourceType = "test",
                ContentHash = "hash1",
                PublishedAt = DateTimeOffset.UtcNow.ToString("O"),
                BlockKind = 0, // Text
                HeadingLevel = null,
                ListType = null,
                ImageUri = null
            },
            new TextParagraph
            {
                Key = "2",
                DocumentUri = "test://document1",
                ParagraphId = "2",
                Text = "This is the second test paragraph about stock market trends and financial analysis.",
                Order = 1,
                Section = "Analysis",
                SourceType = "test",
                ContentHash = "hash2",
                PublishedAt = DateTimeOffset.UtcNow.ToString("O"),
                BlockKind = 0, // Text
                HeadingLevel = null,
                ListType = null,
                ImageUri = null
            }
        };

        // Act - Store paragraphs
        foreach (var paragraph in paragraphs)
        {
            paragraph.TextEmbedding = await _embeddingGenerator.GenerateAsync(paragraph.Text);
            // Set empty ImageEmbedding to avoid SQLite NULL issues
            paragraph.ImageEmbedding = new Embedding<float>(new float[1024]);
            await collection.UpsertAsync(paragraph);
        }

        // Assert - Verify storage
        var storedParagraph = await collection.GetAsync("1");
        Assert.IsNotNull(storedParagraph);
        Assert.AreEqual("1", storedParagraph.Key);
        Assert.AreEqual("This is the first test paragraph about artificial intelligence and machine learning.", storedParagraph.Text);


        // Act - Search for similar paragraphs using vector search
        var searchVector = await _embeddingGenerator.GenerateAsync("first test paragraph");

        var vectorSearchResults = collection.SearchAsync(searchVector, 1, new VectorSearchOptions<TextParagraph>
        {
            VectorProperty = r => r.TextEmbedding
        });
        var vectorSearchResultsList = await vectorSearchResults.ToListAsync();

        Assert.IsNotNull(vectorSearchResultsList);
        Assert.AreEqual(1, vectorSearchResultsList.Count);

        // 直接验证基本的存储和检索功能
        var secondParagraph = await collection.GetAsync("2");

        // Assert - Verify search results (验证基本的存储功能)
        Assert.IsNotNull(secondParagraph);
        Assert.AreEqual("2", secondParagraph.Key);
        Assert.IsTrue(secondParagraph.Text.Contains("stock market"));

        // 验证向量嵌入已正确存储
        Assert.IsNotNull(secondParagraph.TextEmbedding);
        Assert.AreEqual(1024, secondParagraph.TextEmbedding.Vector.Length);
    }
}