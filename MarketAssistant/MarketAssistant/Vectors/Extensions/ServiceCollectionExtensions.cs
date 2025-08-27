using MarketAssistant.Vectors.Interfaces;
using MarketAssistant.Vectors.Services;
using MarketAssistant.Infrastructure;

namespace MarketAssistant.Vectors.Extensions;

/// <summary>
/// Vectors 依赖注入扩展。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册与 RAG 相关的服务。
    /// </summary>
    public static IServiceCollection AddRagServices(this IServiceCollection services)
    {
        services.AddSingleton<ITextCleaningService, TextCleaningService>();
        services.AddSingleton<ITextChunkingService, TextChunkingService>();
        services.AddSingleton<IQueryRewriteService, QueryRewriteService>();
        services.AddSingleton<IRagIngestionService, RagIngestionService>();

        // 多模态：使用 CLIP 服务替换占位实现
        services.AddSingleton<IImageEmbeddingService, ClipImageEmbeddingService>();
        services.AddSingleton<IImageStorageService, LocalImageStorageService>();
        services.AddSingleton<IDocumentBlockReader, PdfBlockReader>();
        services.AddSingleton<IDocumentBlockReader, DocxBlockReader>();

        // 注册重排服务 - 使用装饰器模式实现降级
        services.AddKeyedSingleton<IRerankerService, OnnxCrossEncoderRerankerService>("primary");
        services.AddKeyedSingleton<IRerankerService, RerankerService>("fallback");
        services.AddSingleton<IRerankerService, FallbackRerankerService>();

        services.AddSingleton<RetrievalOrchestrator>();
        services.AddSingleton<IWebTextSearchFactory, WebTextSearchFactory>();
        services.AddSingleton<GroundingSearchPlugin>();

        // 使用 Keyed Services 注册多实现，按扩展名作为 key
        services.AddKeyedSingleton<IRawDocumentReader, PdfRawReader>("pdf");
        services.AddKeyedSingleton<IRawDocumentReader, DocxRawReader>("docx");
        return services;
    }
}


