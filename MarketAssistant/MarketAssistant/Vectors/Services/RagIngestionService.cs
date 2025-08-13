using MarketAssistant.Vectors.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;

namespace MarketAssistant.Vectors.Services;

/// <summary>
/// RAG 数据摄取：清洗 -> 语义分块 -> 嵌入 -> 入库。
/// </summary>
public class RagIngestionService : IRagIngestionService
{
    private readonly ITextCleaningService _cleaning;
    private readonly ITextChunkingService _chunking;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RagIngestionService> _logger;

    public RagIngestionService(
        ITextCleaningService cleaning,
        ITextChunkingService chunking,
        IServiceProvider serviceProvider,
        ILogger<RagIngestionService> logger)
    {
        _cleaning = cleaning;
        _chunking = chunking;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task IngestFileAsync(
        VectorStoreCollection<string, TextParagraph> collection,
        string filePath,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        using var stream = File.OpenRead(filePath);
        var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        var reader = _serviceProvider.GetKeyedService<IRawDocumentReader>(ext);
        if (reader is null)
        {
            _logger.LogError("No IRawDocumentReader found for extension '{Ext}'. File: {File}", ext, filePath);
            return;
        }

        var allText = reader.ReadAllText(stream);
        var cleaned = _cleaning.Clean(allText);
        var chunks = _chunking.Chunk(filePath, cleaned).ToArray();

        foreach (var chunk in chunks)
        {
            chunk.TextEmbedding = await embeddingGenerator.GenerateAsync(chunk.Text);
            await collection.UpsertAsync(chunk);
        }
    }
}


