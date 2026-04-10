using MarketAssistant.Services;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.ContextProviders;

/// <summary>
/// 用户长期记忆上下文提供者，实现 MAF AIContextProvider 模式。
/// 在每次 Agent 调用前自动将用户偏好、历史分析结论注入到系统消息中，实现个性化分析。
/// </summary>
public class UserMemoryContextProvider : MessageAIContextProvider
{
    private readonly UserMemoryService _memoryService;
    private readonly ILogger<UserMemoryContextProvider> _logger;

    public UserMemoryContextProvider(UserMemoryService memoryService, ILogger<UserMemoryContextProvider> logger)
    {
        _memoryService = memoryService;
        _logger = logger;
    }

    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(
        InvokingContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var memories = await _memoryService.GetAllMemoriesAsync(cancellationToken);
            if (memories.Count == 0)
                return [];

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("## 用户长期记忆");
            sb.AppendLine("以下是用户的偏好和历史分析结论，请在回答中参考：");
            sb.AppendLine();

            string? currentCategory = null;
            foreach (var (category, key, value) in memories)
            {
                if (currentCategory != category)
                {
                    currentCategory = category;
                    sb.AppendLine($"### {category}");
                }
                sb.AppendLine($"- **{key}**: {value}");
            }

            _logger.LogDebug("注入用户记忆上下文，共 {Count} 条记忆", memories.Count);

            return
            [
                new ChatMessage(ChatRole.System, sb.ToString())
            ];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载用户记忆失败，跳过记忆上下文注入");
            return [];
        }
    }
}
