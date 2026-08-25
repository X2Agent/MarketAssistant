using CommunityToolkit.Mvvm.ComponentModel;

namespace MarketAssistant.ViewModels;

/// <summary>
/// MCP 工具白名单勾选项。
/// </summary>
public partial class McpToolSelectionItem : ObservableObject
{
    /// <summary>工具名称。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>工具描述（加载工具列表后填充）。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>是否加入白名单。</summary>
    [ObservableProperty]
    private bool _isSelected;
}
