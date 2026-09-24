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
    /// 角色专属模型 ID；留空表示跟随默认模型。
    /// </summary>
    [ObservableProperty]
    private string _modelIdOverride = string.Empty;

    /// <summary>是否配置了角色专属模型</summary>
    public bool HasModelOverride => !string.IsNullOrWhiteSpace(ModelIdOverride);

    partial void OnModelIdOverrideChanged(string value) =>
        OnPropertyChanged(nameof(HasModelOverride));
}
