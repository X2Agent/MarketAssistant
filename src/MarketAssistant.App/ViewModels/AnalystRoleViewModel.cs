using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels;

public partial class AnalystRoleViewModel : ObservableObject
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>
    /// 该角色可选的模型目录，与设置页"模型 ID"共用同一实例，
    /// 刷新模型目录后各角色下拉项同步更新；为空时仍可手工输入模型 ID。
    /// </summary>
    public ObservableCollection<string> ModelOptions { get; init; } = [];

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isRequired;

    /// <summary>
    /// 是否展示该角色的模型编辑器（由设置页"为每个分析师单独指定模型"总开关驱动）。
    /// 关闭时隐藏下拉输入框，避免默认铺开过多输入框；已有分配通过
    /// <see cref="ModelAssignmentHintText"/> 提示，分配本身始终生效。
    /// </summary>
    [ObservableProperty]
    private bool _showModelEditor;

    /// <summary>
    /// 角色专属模型 ID；留空表示跟随默认模型。
    /// </summary>
    [ObservableProperty]
    private string _modelIdOverride = string.Empty;

    /// <summary>是否配置了角色专属模型</summary>
    public bool HasModelOverride => !string.IsNullOrWhiteSpace(ModelIdOverride);

    /// <summary>编辑器隐藏且存在分配时展示提示（避免用户误以为分配丢失）。</summary>
    public bool ShowModelAssignmentHint => !ShowModelEditor && HasModelOverride;

    /// <summary>编辑器隐藏且有分配时的提示文本；否则为空串。</summary>
    public string ModelAssignmentHintText =>
        ShowModelAssignmentHint ? $"已指定模型：{ModelIdOverride}" : string.Empty;

    partial void OnModelIdOverrideChanged(string value)
    {
        OnPropertyChanged(nameof(HasModelOverride));
        OnPropertyChanged(nameof(ShowModelAssignmentHint));
        OnPropertyChanged(nameof(ModelAssignmentHintText));
    }

    partial void OnShowModelEditorChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowModelAssignmentHint));
        OnPropertyChanged(nameof(ModelAssignmentHintText));
    }
}
