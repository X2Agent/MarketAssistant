namespace MarketAssistant.Agents.PromptConfiguration;

public class AnalystPromptConfig
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public float Temperature { get; set; }
    public float TopP { get; set; }
    public int TopK { get; set; }
    public string Instructions { get; set; } = string.Empty;

    /// <summary>
    /// 创建当前配置的副本，替换 Instructions 字段
    /// </summary>
    public AnalystPromptConfig WithInstructions(string instructions) => new()
    {
        Name = Name,
        DisplayName = DisplayName,
        Description = Description,
        Temperature = Temperature,
        TopP = TopP,
        TopK = TopK,
        Instructions = instructions
    };
}
