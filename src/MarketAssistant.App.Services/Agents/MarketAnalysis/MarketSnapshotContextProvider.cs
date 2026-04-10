using System.Collections.Concurrent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MarketAssistant.Agents.MarketAnalysis;

/// <summary>
/// 市场快照共享状态，通过 MAF AIContextProvider 模式在分析师之间共享市场数据。
/// 避免每位分析师重复获取同一市场数据（如当前价格、成交量等）。
/// </summary>
public class MarketSnapshotContextProvider : MessageAIContextProvider
{
    private readonly ConcurrentDictionary<string, string> _sharedData = new();

    /// <summary>
    /// 设置共享数据项
    /// </summary>
    public void SetData(string key, string value) => _sharedData[key] = value;

    /// <summary>
    /// 获取所有共享数据
    /// </summary>
    public IReadOnlyDictionary<string, string> GetAllData() =>
        new Dictionary<string, string>(_sharedData);

    /// <summary>
    /// 清空共享数据
    /// </summary>
    public void Clear() => _sharedData.Clear();

    protected override ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(
        InvokingContext context, CancellationToken cancellationToken = default)
    {
        if (_sharedData.IsEmpty)
            return new ValueTask<IEnumerable<ChatMessage>>(Enumerable.Empty<ChatMessage>());

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## 市场快照（共享数据）");
        sb.AppendLine("以下市场快照数据由协调器预加载，所有分析师共享，无需重复获取：");
        sb.AppendLine();

        foreach (var (key, value) in _sharedData)
        {
            sb.AppendLine($"- **{key}**: {value}");
        }

        IEnumerable<ChatMessage> messages =
        [
            new ChatMessage(ChatRole.System, sb.ToString())
        ];

        return new ValueTask<IEnumerable<ChatMessage>>(messages);
    }
}
