using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents.Orchestration.GroupChat;
using Microsoft.SemanticKernel.ChatCompletion;

namespace MarketAssistant.Agents;

/// <summary>
/// 分析师群组聊天管理器，负责控制分析师的发言顺序和对话终止条件
/// </summary>
public class AnalysisGroupChatManager : RoundRobinGroupChatManager
{
    /// <summary>
    /// 协调分析师名称
    /// </summary>
    private readonly string CoordinatorAnalystName = nameof(AnalysisAgents.CoordinatorAnalystAgent);
    private readonly string FundamentalAnalystName = nameof(AnalysisAgents.FundamentalAnalystAgent);

    public override ValueTask<GroupChatManagerResult<string>> SelectNextAgent(ChatHistory history, GroupChatTeam team, CancellationToken cancellationToken = default)
    {
        // 判断是否新一轮
        var lastMessage = history.Count > 0 ? history.Last() : null;

        if (IsNewConversationRound(lastMessage))
        {
            // 新一轮由FundamentalAnalyst先发言
            return ValueTask.FromResult(new GroupChatManagerResult<string>(FundamentalAnalystName)
            {
                Reason = "新一轮由基本面分析师先发言"
            });
        }

        //如果最新的消息是Assistant的消息且内容为空，则当前Assistant再次发言
        if (lastMessage?.Role == AuthorRole.Assistant
            && string.IsNullOrEmpty(lastMessage.Content)
            && !string.IsNullOrEmpty(lastMessage.AuthorName))
        {
            return ValueTask.FromResult(new GroupChatManagerResult<string>(lastMessage.AuthorName)
            {
                Reason = "上轮分析结果为空，分析师再次发言"
            });
        }

        // 统计本轮已发言分析师
        var spoken = new HashSet<string>();
        for (int i = history.Count - 1; i >= 0; i--)
        {
            var msg = history[i];
            if (msg.Role == AuthorRole.User)
                break;
            spoken.Add(msg.AuthorName ?? "");
        }

        // 非协调分析师，未发言的
        var available = team
            .Where(a => a.Key != CoordinatorAnalystName && a.Key != FundamentalAnalystName && !spoken.Contains(a.Key))
            .ToList();

        if (available.Count > 0)
        {
            // 任选一个未发言的非协调分析师
            var next = available.First();
            return ValueTask.FromResult(new GroupChatManagerResult<string>(next.Key)
            {
                Reason = "轮到未发言的分析师发言"
            });
        }

        // 如果所有非协调分析师都已发言，轮到协调分析师
        var coordinator = team.FirstOrDefault(a => a.Key == CoordinatorAnalystName);
        return ValueTask.FromResult(new GroupChatManagerResult<string>(CoordinatorAnalystName)
        {
            Reason = "轮到协调分析师总结"
        });
    }

    /// <summary>
    /// 判断是否应该终止对话
    /// </summary>
    public override ValueTask<GroupChatManagerResult<bool>> ShouldTerminate(ChatHistory history, CancellationToken cancellationToken = default)
    {
        // 如果没有历史记录或最后一条消息是用户消息，不终止
        if (history.Count == 0 || history.Last().Role == AuthorRole.User)
        {
            return ValueTask.FromResult(new GroupChatManagerResult<bool>(false)
            {
                Reason = "对话尚未开始或等待分析师回应"
            });
        }

        // 获取最后一条消息
        var lastMessage = history.Last();

        // 如果最后一条消息是协调分析师的总结，则终止对话
        bool shouldTerminate = lastMessage.AuthorName == CoordinatorAnalystName &&
                             !string.IsNullOrEmpty(lastMessage.Content);

        return ValueTask.FromResult(new GroupChatManagerResult<bool>(shouldTerminate)
        {
            Reason = shouldTerminate ? "协调分析师已完成总结" : "等待更多分析意见"
        });
    }

    /// <summary>
    /// 判断是否是新的对话轮次
    /// </summary>
    private bool IsNewConversationRound(ChatMessageContent? lastMessage)
    {
        return lastMessage == null || lastMessage.Role == AuthorRole.User;
    }

    /// <summary>
    /// 仅在聊天终止时调用，以汇总或处理对话的最终结果。
    /// </summary>
    /// <param name="history"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override ValueTask<GroupChatManagerResult<string>> FilterResults(ChatHistory history, CancellationToken cancellationToken = default)
    {
        return base.FilterResults(history, cancellationToken);
    }

    /// <summary>
    /// 检查下一个代理说话之前是否需要用户（人工）输入。 如果为 true，则业务流程暂停以等待用户输入
    /// </summary>
    /// <param name="history"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public override ValueTask<GroupChatManagerResult<bool>> ShouldRequestUserInput(ChatHistory history, CancellationToken cancellationToken = default)
    {
        return base.ShouldRequestUserInput(history, cancellationToken);
    }
}