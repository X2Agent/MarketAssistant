using MarketAssistant.Infrastructure;
using MarketAssistant.Vectors.Interfaces;
using MarketAssistant.Vectors.Services;

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

        // 多模态：使用 CLIP 服务替换占位实现
        services.AddSingleton<IImageEmbeddingService, ClipImageEmbeddingService>();
        services.AddSingleton<IImageStorageService, LocalImageStorageService>();

        // 注册改进的转换器
        services.AddSingleton<IMarkdownConverter, DocxMarkdownConverter>();
        services.AddSingleton<IMarkdownConverter, PdfMarkdownConverter>();

        // 注册转换器工厂
        services.AddSingleton<MarkdownConverterFactory>();

        // 先注册具体的 MarkdownDocumentBlockReader
        services.AddSingleton<MarkdownDocumentBlockReader>();

        // 再注册依赖于它的其他读取器
        services.AddSingleton<IDocumentBlockReader, MarkdownDocumentBlockReader>(provider =>
            provider.GetRequiredService<MarkdownDocumentBlockReader>());
        services.AddSingleton<IDocumentBlockReader, DocxBlockReader>();
        services.AddSingleton<IDocumentBlockReader, PdfBlockReader>();


        // 注册统一的文档块读取器工厂
        services.AddSingleton<DocumentBlockReaderFactory>();

        // 注册重排服务 - 已重构为纯启发式算法，无AI调用成本
        // 专为金融场景优化：信任度评分 + 关键词加成 + 时效性 + 多样性优化
        services.AddSingleton<IRerankerService, RerankerService>();
        services.AddSingleton<IQueryRewriteService, QueryRewriteService>();

        services.AddSingleton<IRetrievalOrchestrator, RetrievalOrchestrator>();
        services.AddSingleton<IWebTextSearchFactory, WebTextSearchFactory>();

        services.AddSingleton<IRagIngestionService, RagIngestionService>();

        return services;
    }
}


