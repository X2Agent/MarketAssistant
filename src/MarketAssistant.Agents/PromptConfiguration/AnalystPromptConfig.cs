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
}
