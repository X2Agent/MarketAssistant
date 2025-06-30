using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;

namespace MarketAssistant.Filtering;

internal class PromptCacheFilter(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        VectorStore vectorStore) : IPromptRenderFilter
{
    /// <summary>
    /// Collection/table name in cache to use.
    /// </summary>
    private const string CollectionName = "llm_responses";

    /// <summary>
    /// Metadata key in function result for cache record id, which is used to overwrite previously cached response.
    /// </summary>
    private const string RecordIdKey = "CacheRecordId";

    public async Task OnPromptRenderAsync(PromptRenderContext context, Func<PromptRenderContext, Task> next)
    {
        // Trigger prompt rendering operation
        await next(context);

        // Get rendered prompt
        var prompt = context.RenderedPrompt!;

        var promptEmbedding = await embeddingGenerator.GenerateAsync(prompt);

        var collection = vectorStore.GetCollection<string, CacheRecord>(CollectionName);
        await collection.EnsureCollectionExistsAsync();

        // Search for similar prompts in cache.
        var searchResult = (await collection.SearchAsync(promptEmbedding, top: 1, cancellationToken: context.CancellationToken)
            .FirstOrDefaultAsync())?.Record;

        // If result exists, return it.
        if (searchResult is not null)
        {
            // Override function result. This will prevent calling LLM and will return result immediately.
            context.Result = new FunctionResult(context.Function, searchResult.Result)
            {
                Metadata = new Dictionary<string, object?> { [RecordIdKey] = searchResult.Id }
            };
        }
    }

    private sealed class CacheRecord
    {
        [VectorStoreKey]
        public string Id { get; set; }="";

        [VectorStoreData]
        public string Prompt { get; set; }="";

        [VectorStoreData]
        public string Result { get; set; }="";

        [VectorStoreVector(Dimensions: 1536)]
        public ReadOnlyMemory<float> PromptEmbedding { get; set; }
    }
}