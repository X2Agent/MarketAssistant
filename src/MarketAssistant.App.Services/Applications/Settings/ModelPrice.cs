using CommunityToolkit.Mvvm.ComponentModel;

namespace MarketAssistant.Applications.Settings;

/// <summary>
/// 模型价格条目：单价按每百万 tokens 的人民币价格计（CNY / 1M tokens）。
/// 用户手工维护（各 provider 定价多变且常变动，不在代码内置死）。
/// 实现 INotifyPropertyChanged 以支持设置页表格的双向编辑绑定。
/// </summary>
public partial class ModelPrice : ObservableObject
{
    /// <summary>服务商 ID（与 <see cref="UserSetting.ProviderId"/> 同一套口径）。</summary>
    [ObservableProperty]
    private string _providerId = string.Empty;

    /// <summary>模型 ID（与服务商 API 使用的模型标识一致）。</summary>
    [ObservableProperty]
    private string _modelId = string.Empty;

    /// <summary>输入价格（元 / 1M tokens）。</summary>
    [ObservableProperty]
    private decimal _inputPricePerMillionTokens;

    /// <summary>输出价格（元 / 1M tokens）。</summary>
    [ObservableProperty]
    private decimal _outputPricePerMillionTokens;

    /// <summary>是否可用作价格匹配（必填字段齐备且价格非负）。</summary>
    public bool IsMatchable =>
        !string.IsNullOrWhiteSpace(ProviderId) &&
        !string.IsNullOrWhiteSpace(ModelId) &&
        InputPricePerMillionTokens >= 0m &&
        OutputPricePerMillionTokens >= 0m;
}
