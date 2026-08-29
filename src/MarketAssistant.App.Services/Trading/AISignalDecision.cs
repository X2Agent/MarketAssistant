using System.Text.Json;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// AI 结构化决策模型：LLM 按约定 JSON schema 输出，本地解析校验后驱动下单与护栏生成。
/// 决策与执行解耦——AI 只给方向、置信度与止盈止损价位，实际下单数量、仓位上限与护栏
/// 全部由本地代码强制计算，AI 无法绕过风控。
/// </summary>
public sealed class AISignalDecision
{
    public const string BuyAction = "BUY";
    public const string SellAction = "SELL";
    public const string HoldAction = "HOLD";

    /// <summary>决策方向：BUY / SELL / HOLD。</summary>
    public string Action { get; init; } = HoldAction;

    /// <summary>置信度（0-100），驱动动态仓位 sizing。</summary>
    public int Confidence { get; init; }

    /// <summary>AI 建议的止损价（可为空，为空时按风险档案兜底生成）。</summary>
    public decimal? StopLossPrice { get; init; }

    /// <summary>AI 建议的止盈价（可为空，为空时按风险档案兜底生成）。</summary>
    public decimal? TakeProfitPrice { get; init; }

    /// <summary>决策理由（写入交易记录的 AIReasoning）。</summary>
    public string? Reason { get; init; }

    public bool IsBuy => Action.Equals(BuyAction, StringComparison.OrdinalIgnoreCase);
    public bool IsSell => Action.Equals(SellAction, StringComparison.OrdinalIgnoreCase);
    public bool IsHold => !IsBuy && !IsSell;
}

/// <summary>
/// AI 决策 JSON 解析器：从 LLM 响应文本中稳健提取首个 JSON 决策对象。
/// 容忍 markdown 代码块包裹、前后说明文字与属性大小写差异。
/// </summary>
public static class AISignalDecisionParser
{
    private static readonly string[] ActionAliases = ["decision", "action"];

    /// <summary>
    /// 尝试从响应文本解析决策。返回 false 表示文本中不存在合法 JSON 决策（视为 HOLD 处理）。
    /// </summary>
    public static bool TryParse(string? responseText, out AISignalDecision? decision)
    {
        decision = null;

        var json = ExtractFirstJsonObject(responseText);
        if (json == null)
            return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            string? action = null;
            foreach (var alias in ActionAliases)
            {
                if (root.TryGetProperty(alias, out var actionEl) && actionEl.ValueKind == JsonValueKind.String)
                {
                    action = actionEl.GetString();
                    break;
                }
            }

            int confidence = 0;
            if (root.TryGetProperty("confidence", out var confidenceEl)
                && confidenceEl.TryGetInt32(out var parsedConfidence))
                confidence = Math.Clamp(parsedConfidence, 0, 100);

            decimal? stopLossPrice = TryReadDecimal(root, "stopLossPrice");
            decimal? takeProfitPrice = TryReadDecimal(root, "takeProfitPrice");
            string? reason = null;
            if (root.TryGetProperty("reason", out var reasonEl) && reasonEl.ValueKind == JsonValueKind.String)
                reason = reasonEl.GetString();

            decision = new AISignalDecision
            {
                Action = action?.Trim().ToUpperInvariant() ?? AISignalDecision.HoldAction,
                Confidence = confidence,
                StopLossPrice = stopLossPrice > 0 ? stopLossPrice : null,
                TakeProfitPrice = takeProfitPrice > 0 ? takeProfitPrice : null,
                Reason = reason
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// 提取文本中首个平衡的 JSON 对象（字符串感知，跳过字符串字面量与转义符）。
    /// </summary>
    private static string? ExtractFirstJsonObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var start = text.IndexOf('{');
        if (start < 0)
            return null;

        var inString = false;
        var depth = 0;
        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            if (inString)
            {
                if (ch == '\\')
                    i++; // 跳过转义字符
                else if (ch == '"')
                    inString = false;
                continue;
            }

            if (ch == '"')
                inString = true;
            else if (ch == '{')
                depth++;
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                    return text[start..(i + 1)];
            }
        }

        return null;
    }

    private static decimal? TryReadDecimal(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var el))
            return null;

        return el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetDecimal(out var value) => value,
            JsonValueKind.String when decimal.TryParse(
                el.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }
}