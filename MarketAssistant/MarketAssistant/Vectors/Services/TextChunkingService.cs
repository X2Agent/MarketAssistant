using MarketAssistant.Vectors.Interfaces;
using Microsoft.SemanticKernel.Text;

namespace MarketAssistant.Vectors.Services;

/// <summary>
/// 使用 Semantic Kernel 的 TextChunker 进行语义分块。
/// 方案：先按行切分，再按段落（带 token 限制与重叠）。
/// </summary>
public class TextChunkingService : ITextChunkingService
{
    /// <summary>
    /// 将清洗后的全文分块为段落，优先使用语义边界（行/段落），并用 token 限制控制长度与重叠。
    /// </summary>
    public IEnumerable<TextParagraph> Chunk(string documentUri, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;

        // 参数：行/段落的 token 限制与重叠（可按模型上下文窗口调整）
        const int maxTokensPerLine = 200;
        const int maxTokensPerParagraph = 400;
        const int overlapTokens = 40;

        // 1) 按行切分（基于换行符的语义边界）
        var lines = TextChunker.SplitPlainTextLines(text, maxTokensPerLine, tokenCounter: null);

        // 2) 将行聚合为段落（带 token 限制与重叠）
        var paragraphs = TextChunker.SplitPlainTextParagraphs(
            lines,
            maxTokensPerParagraph,
            overlapTokens,
            tokenCounter: null);

        foreach (var para in paragraphs)
        {
            if (string.IsNullOrWhiteSpace(para)) continue;
            yield return new TextParagraph
            {
                Key = Guid.NewGuid().ToString(),
                DocumentUri = documentUri,
                ParagraphId = Guid.NewGuid().ToString("N"),
                Text = para.Trim()
            };
        }
    }
}


