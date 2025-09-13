using MarketAssistant.Vectors.Interfaces;
using Microsoft.SemanticKernel.Text;
using System.Security.Cryptography;
using System.Text;

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

        int order = 0;
        foreach (var para in paragraphs)
        {
            if (string.IsNullOrWhiteSpace(para)) continue;
            var trimmed = para.Trim();
            var hash = Sha256Hex(trimmed);
            var key = $"{Sha256Hex(documentUri)}:{order}:{hash[..8]}";

            yield return new TextParagraph
            {
                Key = key,
                DocumentUri = documentUri,
                ParagraphId = order.ToString(),
                Text = trimmed,
                Order = order,
                Section = null,
                SourceType = GetSourceTypeFromUri(documentUri),
                ContentHash = hash,
                PublishedAt = null
            };

            order++;
        }
    }

    private static string GetSourceTypeFromUri(string uri)
        => uri.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? "pdf"
         : uri.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) ? "docx"
         : uri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? "web"
         : "text";

    private static string Sha256Hex(string input)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}


