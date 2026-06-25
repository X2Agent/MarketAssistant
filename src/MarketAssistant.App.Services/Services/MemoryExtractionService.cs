using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace MarketAssistant.Services;

/// <summary>
/// 自动记忆提取服务。
/// 从对话历史中使用 LLM 提取结构化记忆（偏好、结论、纠正等），
/// 自动保存到 UserMemoryService 和 UserKnowledgeGraphService。
/// 借鉴 Hermes 的主动保存 + MemPalace 的 auto-save hook。
/// </summary>
public class MemoryExtractionService
{
    private readonly IChatClientFactory _chatClientFactory;
    private readonly UserMemoryService _memoryService;
    private readonly UserKnowledgeGraphService _kgService;
    private readonly ILogger<MemoryExtractionService> _logger;

    /// <summary>
    /// 触发自动提取的对话轮次间隔
    /// </summary>
    public int ExtractionInterval { get; set; } = 6;

    public MemoryExtractionService(
        IChatClientFactory chatClientFactory,
        UserMemoryService memoryService,
        UserKnowledgeGraphService kgService,
        ILogger<MemoryExtractionService> logger)
    {
        _chatClientFactory = chatClientFactory;
        _memoryService = memoryService;
        _kgService = kgService;
        _logger = logger;
    }

    /// <summary>
    /// 从对话历史中提取记忆并持久化。
    /// 在每 N 轮对话后自动调用，或在压缩前紧急调用。
    /// </summary>
    public async Task ExtractAndSaveAsync(
        IReadOnlyList<ChatMessage> conversationHistory,
        bool isEmergency = false,
        CancellationToken ct = default)
    {
        if (conversationHistory.Count < 2)
            return;

        try
        {
            var extracted = await ExtractMemoriesAsync(conversationHistory, ct);
            if (extracted is null)
                return;

            await PersistExtractedMemoriesAsync(extracted, ct);

            _logger.LogInformation(
                "自动记忆提取完成{Emergency}: {MemoryCount} 条记忆, {RelationCount} 条关系",
                isEmergency ? "（紧急）" : "",
                extracted.Memories?.Count ?? 0,
                extracted.Relations?.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "自动记忆提取失败");
        }
    }

    /// <summary>
    /// 使用 LLM 从对话中提取结构化记忆
    /// </summary>
    private async Task<ExtractedMemories?> ExtractMemoriesAsync(
        IReadOnlyList<ChatMessage> history, CancellationToken ct)
    {
        var chatClient = _chatClientFactory.CreateClient();

        var conversationText = new StringBuilder();
        foreach (var msg in history.TakeLast(20))
        {
            var role = msg.Role == ChatRole.User ? "用户" : "助手";
            var text = msg.Text;
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (text.Length > 500) text = text[..500] + "...";
            conversationText.AppendLine($"【{role}】{text}");
        }

        var prompt = $$"""
            分析以下对话，提取值得长期记住的信息。只提取明确表达的内容，不要推测。

            <conversation>
            {{conversationText}}
            </conversation>

            请以 JSON 格式返回提取结果，仅包含确实值得记住的内容（不要生造）：
            ```json
            {
              "memories": [
                {"category": "preference|correction|conclusion|profile", "key": "简短唯一标识", "value": "简洁的记忆内容"}
              ],
              "relations": [
                {"subject": "主体", "predicate": "关注|持有|分析过|属于行业|影响", "object": "客体"}
              ]
            }
            ```

            规则：
            - memories: 只提取用户明确表达的偏好、被纠正的认知、重要结论、个人信息
            - relations: 只提取用户明确提到的实体关系（持有、关注、行业归属等）
            - 如果没有值得提取的内容，返回空数组
            - key 和 value 用中文，保持简洁
            """;

        var response = await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            new ChatOptions { Temperature = 0.1f, MaxOutputTokens = 500 },
            ct);

        var text2 = response.Text;
        if (string.IsNullOrWhiteSpace(text2))
            return null;

        return ParseExtractedMemories(text2);
    }

    private ExtractedMemories? ParseExtractedMemories(string responseText)
    {
        try
        {
            return LlmJsonExtractor.Deserialize<ExtractedMemories>(responseText);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "解析 LLM 记忆提取响应失败");
            return null;
        }
    }

    private async Task PersistExtractedMemoriesAsync(ExtractedMemories extracted, CancellationToken ct)
    {
        if (extracted.Memories != null)
        {
            foreach (var m in extracted.Memories)
            {
                if (string.IsNullOrWhiteSpace(m.Category) || string.IsNullOrWhiteSpace(m.Key) || string.IsNullOrWhiteSpace(m.Value))
                    continue;
                var (success, error) = await _memoryService.SaveMemoryAsync(m.Category, m.Key, m.Value, ct);
                if (!success)
                    _logger.LogWarning("自动提取记忆保存失败 [{Category}] {Key}: {Error}", m.Category, m.Key, error);
            }
        }

        if (extracted.Relations != null)
        {
            foreach (var r in extracted.Relations)
            {
                if (string.IsNullOrWhiteSpace(r.Subject) || string.IsNullOrWhiteSpace(r.Predicate) || string.IsNullOrWhiteSpace(r.Object))
                    continue;
                await _kgService.AddTripleAsync(r.Subject, r.Predicate, r.Object, ct: ct);
            }
        }
    }
}

internal class ExtractedMemories
{
    public List<ExtractedMemoryItem>? Memories { get; set; }
    public List<ExtractedRelation>? Relations { get; set; }
}

internal class ExtractedMemoryItem
{
    public string Category { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

internal class ExtractedRelation
{
    public string Subject { get; set; } = string.Empty;
    public string Predicate { get; set; } = string.Empty;
    public string Object { get; set; } = string.Empty;
}
