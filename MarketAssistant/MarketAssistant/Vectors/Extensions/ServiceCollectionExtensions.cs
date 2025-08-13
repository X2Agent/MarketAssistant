using MarketAssistant.Vectors.Interfaces;
using MarketAssistant.Vectors.Services;
using Microsoft.Extensions.DependencyInjection;

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
        // 使用 Keyed Services 注册多实现，按扩展名作为 key
        services.AddKeyedSingleton<IRawDocumentReader, PdfRawReader>("pdf");
        services.AddKeyedSingleton<IRawDocumentReader, DocxRawReader>("docx");
        return services;
    }
}


