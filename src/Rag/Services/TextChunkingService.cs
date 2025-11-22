using MarketAssistant.Rag.Interfaces;
using System.Security.Cryptography;

namespace MarketAssistant.Rag.Services;

/// <summary>
/// 使用自定义的 TextChunker 进行语义分块。
/// 方案：先按行切分，再按段落（带 token 限制与重叠）。
/// 代码逻辑源自 Microsoft.SemanticKernel.Text.TextChunker (MIT License)，已做适配以移除 SK 依赖。
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
        var lines = TextChunkerHelper.SplitPlainTextLines(text, maxTokensPerLine);

        // 2) 将行聚合为段落（带 token 限制与重叠）
        var paragraphs = TextChunkerHelper.SplitPlainTextParagraphs(
            lines,
            maxTokensPerParagraph,
            overlapTokens);

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

    /// <summary>
    /// 内部辅助类，移植自 Microsoft.SemanticKernel.Text.TextChunker
    /// </summary>
    private static class TextChunkerHelper
    {
        private static readonly string?[] s_plaintextSplitOptions = ["\n", ".。．", "?!", ";", ":", ",，、", ")]}", " ", "-", null];

        public static List<string> SplitPlainTextLines(string text, int maxTokensPerLine)
        {
            return InternalSplitLines(text, maxTokensPerLine, trim: true, s_plaintextSplitOptions);
        }

        public static List<string> SplitPlainTextParagraphs(IEnumerable<string> lines, int maxTokensPerParagraph, int overlapTokens)
        {
            return InternalSplitTextParagraphs(
                lines.Select(line => line.Replace("\r\n", "\n").Replace('\r', '\n')),
                maxTokensPerParagraph,
                overlapTokens,
                (text, maxTokens) => InternalSplitLines(text, maxTokens, trim: false, s_plaintextSplitOptions));
        }

        private static List<string> InternalSplitLines(string text, int maxTokensPerLine, bool trim, string?[] splitOptions)
        {
            var result = new List<string>();
            text = text.Replace("\r\n", "\n"); // normalize line endings

            // 初始列表包含完整文本
            var currentList = new List<string> { text };

            for (int i = 0; i < splitOptions.Length; i++)
            {
                var newList = new List<string>();
                bool anySplit = false;

                foreach (var line in currentList)
                {
                    ReadOnlySpan<char> separators = splitOptions[i] is null ? default : splitOptions[i].AsSpan();
                    var (splits, wasSplit) = Split(line, maxTokensPerLine, separators, trim);
                    newList.AddRange(splits);
                    if (wasSplit) anySplit = true;
                }

                currentList = newList;
                if (!anySplit && AllLinesFit(currentList, maxTokensPerLine))
                {
                    break;
                }
            }

            return currentList;
        }

        private static bool AllLinesFit(List<string> lines, int maxTokens)
        {
            return lines.All(l => GetTokenCount(l) <= maxTokens);
        }

        private static (List<string>, bool) Split(string input, int maxTokens, ReadOnlySpan<char> separators, bool trim)
        {
            int inputTokenCount = GetTokenCount(input);
            if (inputTokenCount <= maxTokens)
            {
                return (new List<string> { trim ? input.Trim() : input }, false);
            }

            bool inputWasSplit = true;
            var result = new List<string>();

            int half = input.Length / 2;
            int cutPoint = -1;

            if (separators.IsEmpty)
            {
                cutPoint = half;
            }
            else if (input.Length > 2)
            {
                int pos = 0;
                while (true)
                {
                    int index = input.AsSpan(pos, input.Length - 1 - pos).IndexOfAny(separators);
                    if (index < 0) break;

                    index += pos;

                    if (Math.Abs(half - index) < Math.Abs(half - cutPoint))
                    {
                        cutPoint = index + 1;
                    }
                    pos = index + 1;
                }
            }

            if (cutPoint > 0)
            {
                var firstHalf = input.Substring(0, cutPoint);
                var secondHalf = input.Substring(cutPoint);

                if (trim)
                {
                    firstHalf = firstHalf.Trim();
                    secondHalf = secondHalf.Trim();
                }

                // Recursion
                var (splits1, _) = Split(firstHalf, maxTokens, separators, trim);
                result.AddRange(splits1);
                var (splits2, _) = Split(secondHalf, maxTokens, separators, trim);
                result.AddRange(splits2);

                return (result, inputWasSplit);
            }

            // Fallback if no split point found but still too long (should rarely happen with empty separators)
            return (new List<string> { trim ? input.Trim() : input }, false);
        }

        private static List<string> InternalSplitTextParagraphs(
            IEnumerable<string> lines,
            int maxTokensPerParagraph,
            int overlapTokens,
            Func<string, int, List<string>> longLinesSplitter)
        {
            if (maxTokensPerParagraph <= 0) throw new ArgumentException("maxTokensPerParagraph should be a positive number");
            if (maxTokensPerParagraph <= overlapTokens) throw new ArgumentException("overlapTokens cannot be larger than maxTokensPerParagraph");

            var linesList = lines.ToList();
            if (linesList.Count == 0) return new List<string>();

            // Split long lines first
            var truncatedLines = linesList.SelectMany(line =>
                GetTokenCount(line) > maxTokensPerParagraph
                ? longLinesSplitter(line, maxTokensPerParagraph)
                : new List<string> { line });

            var paragraphs = BuildParagraph(truncatedLines, maxTokensPerParagraph);
            var processedParagraphs = ProcessParagraphs(paragraphs, maxTokensPerParagraph, overlapTokens, longLinesSplitter);

            return processedParagraphs;
        }

        private static List<string> BuildParagraph(IEnumerable<string> truncatedLines, int maxTokensPerParagraph)
        {
            StringBuilder paragraphBuilder = new();
            List<string> paragraphs = new();

            foreach (string line in truncatedLines)
            {
                if (paragraphBuilder.Length > 0)
                {
                    string currentPara = paragraphBuilder.ToString();
                    int currentCount = GetTokenCount(currentPara) + GetTokenCount(line) + 1; // +1 for newline approximation

                    if (currentCount >= maxTokensPerParagraph)
                    {
                        paragraphs.Add(currentPara.Trim());
                        paragraphBuilder.Clear();
                    }
                }
                paragraphBuilder.AppendLine(line);
            }

            if (paragraphBuilder.Length > 0)
            {
                paragraphs.Add(paragraphBuilder.ToString().Trim());
            }

            return paragraphs;
        }

        private static List<string> ProcessParagraphs(
            List<string> paragraphs,
            int maxTokensPerParagraph,
            int overlapTokens,
            Func<string, int, List<string>> longLinesSplitter)
        {
            // Balance last two paragraphs if needed
            if (paragraphs.Count > 1)
            {
                var lastParagraph = paragraphs[^1];
                var secondLastParagraph = paragraphs[^2];

                if (GetTokenCount(lastParagraph) < maxTokensPerParagraph / 4)
                {
                    var combined = secondLastParagraph + "\n" + lastParagraph;
                    var combinedTokens = GetTokenCount(combined);
                    if (combinedTokens <= maxTokensPerParagraph)
                    {
                        paragraphs[^2] = combined;
                        paragraphs.RemoveAt(paragraphs.Count - 1);
                    }
                }
            }

            var processedParagraphs = new List<string>();
            var sb = new StringBuilder();

            for (int i = 0; i < paragraphs.Count; i++)
            {
                sb.Clear();
                var paragraph = paragraphs[i];

                if (overlapTokens > 0 && i < paragraphs.Count - 1)
                {
                    var nextParagraph = paragraphs[i + 1];
                    // Use longLinesSplitter to find a good split point for overlap
                    var split = longLinesSplitter(nextParagraph, overlapTokens);

                    sb.Append(paragraph);
                    if (split.Count > 0)
                    {
                        sb.Append("\n").Append(split[0]);
                    }
                }
                else
                {
                    sb.Append(paragraph);
                }

                processedParagraphs.Add(sb.ToString());
            }

            return processedParagraphs;
        }

        private static int GetTokenCount(string input)
        {
            // Default approximation: length / 4
            return input.Length / 4;
        }
    }
}


