using MarketAssistant.Rag;

namespace MarketAssistant.Rag.Interfaces;

/// <summary>
/// 语义分块服务接口。
/// 使用 Microsoft.SemanticKernel.Text 进行语义友好的文本拆分。
/// </summary>
public interface ITextChunkingService
{
    /// <summary>
    /// 将大段文本拆分为语义片段。
    /// </summary>
    /// <param name="documentUri">源文档标识</param>
    /// <param name="text">清洗后的整段文本</param>
    /// <returns>语义片段集合</returns>
    IEnumerable<TextParagraph> Chunk(string documentUri, string text);
}


