using MarketAssistant.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Text;

namespace MarketAssistant.Agents.Tools;

/// <summary>
/// 用户长期记忆管理工具，暴露给 Agent 用于主动保存、查询和删除记忆。
/// Agent 应在学习到用户偏好、被纠正、发现重要结论时主动调用。
/// </summary>
public class MemoryManagementTools
{
    private readonly UserMemoryService _memoryService;
    private readonly ILogger<MemoryManagementTools> _logger;

    public MemoryManagementTools(UserMemoryService memoryService, ILogger<MemoryManagementTools> logger)
    {
        _memoryService = memoryService;
        _logger = logger;
    }

    [Description("保存一条用户记忆。当你学习到用户的投资偏好、风格习惯、被纠正的认知、或重要分析结论时，主动调用此工具。" +
                 "category 分类包括: preference(偏好)、correction(纠正)、conclusion(结论)、profile(用户画像)、other(其它)。" +
                 "key 是唯一标识，如 '风险偏好' 或 '贵州茅台观点'。value 是记忆内容，尽量简洁。")]
    public async Task<string> SaveMemoryAsync(
        [Description("记忆分类: preference/correction/conclusion/profile/other")] string category,
        [Description("记忆键名，简短唯一标识")] string key,
        [Description("记忆内容，保持简洁信息密集")] string value)
    {
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            return "参数不能为空。";

        var (success, error) = await _memoryService.SaveMemoryAsync(category, key, value);
        if (!success)
        {
            _logger.LogWarning("保存记忆失败: {Error}", error);
            return $"保存失败: {error}";
        }

        _logger.LogInformation("Agent 保存记忆 [{Category}] {Key}", category, key);
        return $"已保存记忆: [{category}] {key}";
    }

    [Description("删除一条过时或错误的用户记忆。当用户偏好变更、之前的结论不再适用时调用。")]
    public async Task<string> DeleteMemoryAsync(
        [Description("记忆分类")] string category,
        [Description("记忆键名")] string key)
    {
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(key))
            return "参数不能为空。";

        await _memoryService.DeleteMemoryAsync(category, key);
        _logger.LogInformation("Agent 删除记忆 [{Category}] {Key}", category, key);
        return $"已删除记忆: [{category}] {key}";
    }

    [Description("查询当前保存的用户记忆。可指定分类查询，或不传分类查询全部。用于确认已有记忆、避免重复保存。")]
    public async Task<string> GetMemoriesAsync(
        [Description("可选的记忆分类筛选，为空则返回全部")] string? category = null)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(category))
        {
            var memories = await _memoryService.GetMemoriesAsync(category);
            if (memories.Count == 0)
                return $"分类 [{category}] 下没有记忆条目。";

            sb.AppendLine($"## [{category}] 分类记忆 ({memories.Count} 条)");
            foreach (var (k, v) in memories)
                sb.AppendLine($"- **{k}**: {v}");
        }
        else
        {
            var allMemories = await _memoryService.GetAllMemoriesAsync();
            if (allMemories.Count == 0)
                return "当前没有任何记忆条目。";

            var usage = await _memoryService.GetUsageAsync();
            sb.AppendLine($"## 用户记忆 ({usage.EntryCount}/{usage.MaxEntryCount} 条, {usage.TotalChars}/{usage.MaxTotalChars} 字符)");

            string? currentCategory = null;
            foreach (var (cat, k, v) in allMemories)
            {
                if (currentCategory != cat)
                {
                    currentCategory = cat;
                    sb.AppendLine($"### {cat}");
                }
                sb.AppendLine($"- **{k}**: {v}");
            }
        }

        return sb.ToString();
    }

    [Description("设置一条记忆的优先级。高优先级(>=1)的记忆会始终加载到上下文中（L1层），确保你始终记住最重要的信息。")]
    public async Task<string> SetMemoryPriorityAsync(
        [Description("记忆分类")] string category,
        [Description("记忆键名")] string key,
        [Description("优先级: 0=普通, 1=高优先级(始终加载)")] int priority)
    {
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(key))
            return "参数不能为空。";

        if (priority < 0) priority = 0;

        var exists = await _memoryService.GetMemoriesAsync(category);
        if (!exists.ContainsKey(key))
            return $"记忆条目 [{category}] {key} 不存在。";

        await _memoryService.SetPriorityAsync(category, key, priority);
        _logger.LogInformation("Agent 设置记忆优先级 [{Category}] {Key} → {Priority}", category, key, priority);
        return $"已设置 [{category}] {key} 优先级为 {priority}" + (priority >= 1 ? "（始终加载）" : "（普通）");
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(SaveMemoryAsync);
        yield return AIFunctionFactory.Create(DeleteMemoryAsync);
        yield return AIFunctionFactory.Create(GetMemoriesAsync);
        yield return AIFunctionFactory.Create(SetMemoryPriorityAsync);
    }
}
