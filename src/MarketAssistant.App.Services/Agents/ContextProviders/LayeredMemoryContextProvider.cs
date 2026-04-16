using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Extensions;
using MarketAssistant.Services;
using MarketAssistant.Services.Settings;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text;

namespace MarketAssistant.Agents.ContextProviders;

/// <summary>
/// 分层记忆上下文提供者，实现 L0-L1 始终加载策略。
/// L0: 用户身份 (~50 tokens) — 始终加载
/// L1: 关键事实 (~200 tokens) — 高优先级记忆始终加载
/// L2: 工作记忆 — 由 MarketChatSession 维护的会话上下文
/// L3: 按需召回 — 通过 Agent 工具调用 (SessionSearch / RAG / KG)
/// 此 Provider 合并了原 UserMemoryContextProvider 和 InvestmentPreferenceContextProvider 的职责。
/// </summary>
public class LayeredMemoryContextProvider : MessageAIContextProvider
{
    private readonly UserMemoryService _memoryService;
    private readonly IUserSettingService _settingService;
    private readonly ILogger<LayeredMemoryContextProvider> _logger;

    public LayeredMemoryContextProvider(
        UserMemoryService memoryService,
        IUserSettingService settingService,
        ILogger<LayeredMemoryContextProvider> logger)
    {
        _memoryService = memoryService;
        _settingService = settingService;
        _logger = logger;
    }

    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(
        InvokingContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var sb = new StringBuilder();

            // ── L0: 用户身份 (~50 tokens) ──
            BuildL0Identity(sb);

            // ── L1: 关键事实 (~200 tokens) ──
            await BuildL1CriticalFactsAsync(sb, cancellationToken);

            if (sb.Length == 0)
                return [];

            _logger.LogDebug("分层记忆注入完成，总长度: {Length} 字符", sb.Length);

            return
            [
                new ChatMessage(ChatRole.System, sb.ToString())
            ];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "分层记忆上下文构建失败，跳过注入");
            return [];
        }
    }

    /// <summary>
    /// L0: 用户身份层 — 从 UserSetting 提取基础身份信息
    /// </summary>
    private void BuildL0Identity(StringBuilder sb)
    {
        var setting = _settingService.CurrentSetting;

        sb.AppendLine("## 用户概况");

        var marketName = setting.CurrentMarketType switch
        {
            MarketType.AShare => "A股",
            MarketType.Crypto => "加密货币",
            _ => setting.CurrentMarketType.ToString()
        };
        sb.AppendLine($"- 主要关注市场: {marketName}");

        var pref = setting.InvestmentPreference;
        sb.AppendLine($"- 风险承受能力: {pref.RiskTolerance.GetDescription()}");
        sb.AppendLine($"- 投资期限: {pref.InvestmentHorizon.GetDescription()}");
        sb.AppendLine();
    }

    /// <summary>
    /// L1: 关键事实层 — 从 UserMemoryService 提取高优先级记忆
    /// </summary>
    private async Task BuildL1CriticalFactsAsync(StringBuilder sb, CancellationToken ct)
    {
        var highPriorityMemories = await _memoryService.GetHighPriorityMemoriesAsync(minPriority: 1, ct: ct);

        if (highPriorityMemories.Count == 0)
        {
            var allMemories = await _memoryService.GetAllMemoriesAsync(ct);
            if (allMemories.Count == 0)
                return;

            // 无高优先级记忆时，加载最近的关键条目（上限 10 条，约 200 tokens）
            highPriorityMemories = allMemories.Take(10).ToList();
        }

        sb.AppendLine("## 关键记忆");

        string? currentCategory = null;
        foreach (var (category, key, value) in highPriorityMemories)
        {
            if (currentCategory != category)
            {
                currentCategory = category;
                sb.AppendLine($"### {category}");
            }
            sb.AppendLine($"- **{key}**: {value}");
        }
        sb.AppendLine();
    }
}
