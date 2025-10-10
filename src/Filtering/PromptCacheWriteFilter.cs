using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using SK = Microsoft.SemanticKernel;

namespace MarketAssistant.Filtering;

/// <summary>
/// 在函数执行完成后，将 LLM 返回结果写入向量缓存。
/// 需要配合 <see cref="PromptCacheFilter"/> 使用，其会在参数中填充 RecordId 与 RenderedPrompt。
/// </summary>
internal sealed class PromptCacheWriteFilter(
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        VectorStore vectorStore) : CacheBaseFilter, SK.IFunctionInvocationFilter
{
    public async Task OnFunctionInvocationAsync(SK.FunctionInvocationContext context, Func<SK.FunctionInvocationContext, Task> next)
    {
        await next(context);

        var resultString = context.Result.GetValue<string>();

        // If there was any rendered prompt, cache it together with LLM result for future calls.
        if (!string.IsNullOrEmpty(context.Result.RenderedPrompt) && !string.IsNullOrWhiteSpace(resultString))
        {
            var collection = vectorStore.GetCollection<string, CacheRecord>(CollectionName);
            await collection.EnsureCollectionExistsAsync();

            // Get cache record id if result was cached previously or generate new id.
            var recordId = context.Result.Metadata?.GetValueOrDefault(RecordIdKey, Guid.NewGuid().ToString()) as string;
            var prompt = context.Result.RenderedPrompt;
            var embedding = await embeddingGenerator.GenerateAsync(prompt);
            var record = new CacheRecord
            {
                Id = recordId!,
                Prompt = prompt,
                Result = resultString!,
                PromptEmbedding = embedding.Vector
            };

            await collection.UpsertAsync(record, context.CancellationToken);
        }
    }
}

