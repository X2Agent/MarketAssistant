using System.Collections.Concurrent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MarketAssistant.Agents.MarketAnalysis;

/// <summary>
/// 运行级共享元数据上下文：通过 MAF AIContextProvider 模式向本次 Run 的
/// 所有分析师注入公共信息（分析标的、分析时间）。
/// 注意：本类不预加载任何行情数据；行情仍由各分析师通过工具按需获取。
/// </summary>
public class MarketSnapshotContextProvider : MessageAIContextProvider
{
    private readonly ConcurrentDictionary<string, string> _sharedData = new();

    public void SetData(string key, string value) => _sharedData[key] = value;

    protected override ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(
        InvokingContext context, CancellationToken cancellationToken = default)
    {
        if (_sharedData.IsEmpty)
            return new ValueTask<IEnumerable<ChatMessage>>([]);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## 本次分析公共信息");
        sb.AppendLine("以下信息由工作流注入，本次运行中的所有分析师共享：");
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
