namespace MarketAssistant.Rag.Services;

/// <summary>
/// SourceType 推断统一入口：从文档路径/documentUri 推断来源类型。
/// 全仓（TextChunkingService、DocumentBlockMapper 等）必须使用同一推断逻辑，
/// 保证同一文档的块 SourceType 取值域一致。
/// </summary>
public static class RagSourceType
{
    /// <summary>
    /// 按文件扩展名（其次 http 前缀）推断来源类型。
    /// </summary>
    public static string InferFromPath(string filePath)
        => Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".pdf" => "pdf",
            ".docx" => "docx",
            ".md" or ".markdown" => "markdown",
            ".txt" => "text",
            _ when filePath.StartsWith("http", StringComparison.OrdinalIgnoreCase) => "web",
            // 无扩展名（纯文本 URI 等）默认按 text 处理
            _ => "text"
        };
}
