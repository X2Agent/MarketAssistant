namespace MarketAssistant.Agents.PromptConfiguration;

/// <summary>
/// 分析师提示词配置模型
/// </summary>
public class AnalystPromptConfig
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public float Temperature { get; set; }
    public float TopP { get; set; }
    public int TopK { get; set; }
    public string Instructions { get; set; } = string.Empty;
}
