﻿using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;

namespace MarketAssistant.Filtering;

internal class PromptCacheFilter(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        VectorStore vectorStore) : CacheBaseFilter, IPromptRenderFilter
{
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

        // If result exists, check TTL and return if valid; if expired, delete it.
        if (searchResult is not null)
        {
            if (IsExpired(searchResult))
            {
                await collection.DeleteAsync(searchResult.Id, context.CancellationToken);
            }
            else
            {
                // Override function result. This will prevent calling LLM and will return result immediately.
                context.Result = new FunctionResult(context.Function, searchResult.Result)
                {
                    Metadata = new Dictionary<string, object?> { [RecordIdKey] = searchResult.Id }
                };
            }
        }
    }
}