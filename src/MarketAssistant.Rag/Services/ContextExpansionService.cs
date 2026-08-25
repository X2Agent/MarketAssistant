using MarketAssistant.Rag.Interfaces;

namespace MarketAssistant.Rag.Services;

/// <summary>
/// 邻接上下文扩展实现（P1-02）：纯内存操作，邻接来源为本次检索合并候选池。
/// </summary>
public sealed class ContextExpansionService : IContextExpansionService
{
    /// <inheritdoc />
    public string BuildExpandedText(
        RagSearchCandidate selected,
        IReadOnlyList<RagSearchCandidate> pool,
        int window = 1,
        int maxNeighborChars = 300)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(pool);

        if (window <= 0)
            return selected.Record.Text;

        var documentUri = selected.Record.DocumentUri;
        var order = selected.Record.Order;

        var neighbors = pool
            .Where(c => !string.Equals(c.Record.Key, selected.Record.Key, StringComparison.Ordinal))
            .Where(c => string.Equals(c.Record.DocumentUri, documentUri, StringComparison.Ordinal))
            .Where(c => Math.Abs(c.Record.Order - order) <= window && c.Record.Order != order)
            .OrderBy(c => c.Record.Order)
            .ToList();

        if (neighbors.Count == 0)
            return selected.Record.Text;

        var previous = neighbors.Where(n => n.Record.Order < order).ToList();
        var next = neighbors.Where(n => n.Record.Order > order).ToList();

        var parts = new List<string>();
        if (previous.Count > 0)
            parts.Add($"【上文】{Truncate(string.Join("\n", previous.Select(n => n.Record.Text)), maxNeighborChars)}");

        parts.Add(selected.Record.Text);

        if (next.Count > 0)
            parts.Add($"【下文】{Truncate(string.Join("\n", next.Select(n => n.Record.Text)), maxNeighborChars)}");

        return string.Join("\n", parts);
    }

    private static string Truncate(string text, int maxChars)
    {
        text = text.Trim();
        return text.Length <= maxChars ? text : text[..maxChars] + "…";
    }
}