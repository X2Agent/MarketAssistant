using MarketAssistant.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Text;

namespace MarketAssistant.Agents.Tools;

/// <summary>
/// 知识图谱工具，允许 Agent 记录和查询实体间关系。
/// 适用于记录：用户持有/关注的标的、分析过的股票/币种、行业关联、重大事件影响。
/// </summary>
public class KnowledgeGraphTools
{
    private readonly UserKnowledgeGraphService _kgService;
    private readonly ILogger<KnowledgeGraphTools> _logger;

    public KnowledgeGraphTools(UserKnowledgeGraphService kgService, ILogger<KnowledgeGraphTools> logger)
    {
        _kgService = kgService;
        _logger = logger;
    }

    [Description("记录一条实体关系。适用场景：用户持有/关注某标的、某事件影响某行业、分析过某标的等。" +
                 "predicate 常用值: 关注/持有/分析过/属于行业/影响/相关联。")]
    public async Task<string> AddRelationAsync(
        [Description("主体实体，如 '用户'、'贵州茅台'、'降息'")] string subject,
        [Description("关系类型，如 '关注'、'持有'、'分析过'、'属于行业'、'影响'")] string predicate,
        [Description("客体实体，如 '贵州茅台'、'白酒行业'、'银行板块'")] string obj,
        [Description("关系生效日期，格式 yyyy-MM-dd，默认今天")] string? validFrom = null,
        [Description("可选的附加说明")] string? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(predicate) || string.IsNullOrWhiteSpace(obj))
            return "subject、predicate、object 不能为空。";

        await _kgService.AddTripleAsync(subject, predicate, obj, validFrom, metadata);
        _logger.LogInformation("Agent 添加知识图谱: {S} --[{P}]--> {O}", subject, predicate, obj);
        return $"已记录关系: {subject} --[{predicate}]--> {obj}";
    }

    [Description("查询某个实体的所有当前有效关系。用于了解用户关注的标的、某标的的关联信息等。")]
    public async Task<string> QueryEntityAsync(
        [Description("要查询的实体名称")] string entity,
        [Description("查询截止日期(yyyy-MM-dd)，默认今天")] string? asOf = null)
    {
        if (string.IsNullOrWhiteSpace(entity))
            return "请提供实体名称。";

        var triples = await _kgService.QueryEntityAsync(entity, asOf);
        if (triples.Count == 0)
            return $"未找到与 \"{entity}\" 相关的关系记录。";

        var sb = new StringBuilder();
        sb.AppendLine($"## {entity} 的关系网络 ({triples.Count} 条)");
        foreach (var t in triples)
        {
            var status = t.IsActive ? "" : " [已过期]";
            var meta = string.IsNullOrEmpty(t.Metadata) ? "" : $" ({t.Metadata})";
            sb.AppendLine($"- {t.Subject} --[{t.Predicate}]--> {t.Object} (自 {t.ValidFrom}){status}{meta}");
        }
        return sb.ToString();
    }

    [Description("使一条关系过期。当用户不再持有某标的、某关系不再成立时调用。")]
    public async Task<string> InvalidateRelationAsync(
        [Description("主体")] string subject,
        [Description("关系类型")] string predicate,
        [Description("客体")] string obj,
        [Description("过期日期(yyyy-MM-dd)，默认今天")] string? ended = null)
    {
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(predicate) || string.IsNullOrWhiteSpace(obj))
            return "参数不能为空。";

        await _kgService.InvalidateAsync(subject, predicate, obj, ended);
        return $"已标记过期: {subject} --[{predicate}]--> {obj}";
    }

    [Description("获取某实体的完整时间线，包含历史和当前关系。用于追溯投资历史。")]
    public async Task<string> GetTimelineAsync(
        [Description("要查询时间线的实体名称")] string entity)
    {
        if (string.IsNullOrWhiteSpace(entity))
            return "请提供实体名称。";

        var triples = await _kgService.TimelineAsync(entity);
        if (triples.Count == 0)
            return $"未找到 \"{entity}\" 的历史记录。";

        var sb = new StringBuilder();
        sb.AppendLine($"## {entity} 时间线 ({triples.Count} 条记录)");
        foreach (var t in triples)
        {
            var period = t.ValidTo != null ? $"{t.ValidFrom} ~ {t.ValidTo}" : $"{t.ValidFrom} ~ 至今";
            sb.AppendLine($"- [{period}] {t.Subject} --[{t.Predicate}]--> {t.Object}");
        }
        return sb.ToString();
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(AddRelationAsync);
        yield return AIFunctionFactory.Create(QueryEntityAsync);
        yield return AIFunctionFactory.Create(InvalidateRelationAsync);
        yield return AIFunctionFactory.Create(GetTimelineAsync);
    }
}
