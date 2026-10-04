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

/// <summary>
/// 内置模型参考价表。
/// </summary>
/// <remarks>
/// <para>
/// 用途：让「成本可见」在开箱状态下即生效——未配置价格时用户只能看到 Token 计数，
/// 而 P0-2 已对外承诺展示货币成本，二者不一致会形成信任落差。
/// </para>
/// <para>
/// <b>重要：这些是参考价，不是报价。</b>各服务商定价频繁调整（促销、缓存命中计费差异、
/// 阶梯价），把价格内置为「事实」会给用户错误的成本预期。因此：
/// </para>
/// <list type="number">
///   <item>仅覆盖常见模型，未覆盖的仍按"未定价"处理，不猜价；</item>
///   <item>价格随 <see cref="SnapshotDate"/> 一并公示，设置页明确提示用户核对后自行维护；</item>
///   <item>用户一旦手工维护某行，即以用户值为准，内置值不再参与匹配。</item>
/// </list>
/// </remarks>
public static class DefaultModelPriceCatalog
{
    /// <summary>
    /// 参考价快照日期。UI 必须展示该日期，让用户知道价格可能已过期。
    /// 更新价格时同步更新此常量与下表。
    /// </summary>
    public const string SnapshotDate = "2026-09";

    /// <summary>
    /// 参考价（人民币元 / 1M tokens）。顺序即设置页展示顺序。
    /// 数值取自各服务商公开定价页的常规档位，不含促销与缓存优惠。
    /// </summary>
    public static IReadOnlyList<ModelPrice> Defaults { get; } =
    [
        // DeepSeek：区分 deepseek-chat（V3）与 deepseek-reasoner（R1）两条线
        new() { ProviderId = "DeepSeek", ModelId = "deepseek-chat", InputPricePerMillionTokens = 2.00m, OutputPricePerMillionTokens = 8.00m },
        new() { ProviderId = "DeepSeek", ModelId = "deepseek-reasoner", InputPricePerMillionTokens = 4.00m, OutputPricePerMillionTokens = 16.00m },

        // 智谱 GLM 系列
        new() { ProviderId = "Zhipu", ModelId = "glm-4-flash", InputPricePerMillionTokens = 0.60m, OutputPricePerMillionTokens = 2.40m },
        new() { ProviderId = "Zhipu", ModelId = "glm-4-plus", InputPricePerMillionTokens = 4.00m, OutputPricePerMillionTokens = 12.00m },

        // 通义千问
        new() { ProviderId = "Qwen", ModelId = "qwen-plus", InputPricePerMillionTokens = 0.80m, OutputPricePerMillionTokens = 2.00m },
        new() { ProviderId = "Qwen", ModelId = "qwen-turbo", InputPricePerMillionTokens = 0.30m, OutputPricePerMillionTokens = 0.60m },

        // 月之暗面 Kimi
        new() { ProviderId = "Moonshot", ModelId = "moonshot-v1-8k", InputPricePerMillionTokens = 1.20m, OutputPricePerMillionTokens = 1.20m },
        new() { ProviderId = "Moonshot", ModelId = "kimi-k2-0711-preview", InputPricePerMillionTokens = 4.00m, OutputPricePerMillionTokens = 16.00m },

        // 硅基流动（README 推荐的开源模型托管）
        new() { ProviderId = "SiliconFlow", ModelId = "Qwen/Qwen3-32B", InputPricePerMillionTokens = 0.60m, OutputPricePerMillionTokens = 0.60m },
        new() { ProviderId = "SiliconFlow", ModelId = "deepseek-ai/DeepSeek-V3", InputPricePerMillionTokens = 2.00m, OutputPricePerMillionTokens = 8.00m },
        new() { ProviderId = "SiliconFlow", ModelId = "deepseek-ai/DeepSeek-R1", InputPricePerMillionTokens = 4.00m, OutputPricePerMillionTokens = 16.00m },

        // OpenAI（美元价换算为人民币，按 7.2 汇率折算，仅供量级参考）
        new() { ProviderId = "OpenAI", ModelId = "gpt-4o-mini", InputPricePerMillionTokens = 1.30m, OutputPricePerMillionTokens = 4.90m },
        new() { ProviderId = "OpenAI", ModelId = "gpt-4o", InputPricePerMillionTokens = 180.00m, OutputPricePerMillionTokens = 720.00m }
    ];

    /// <summary>
    /// 生成默认价格表的新实例副本，避免多用户/多次初始化共享同一可变对象
    /// （<see cref="ModelPrice"/> 实现 <c>INotifyPropertyChanged</c>，设置页会就地编辑）。
    /// </summary>
    public static List<ModelPrice> CreateDefaults()
        => [.. Defaults.Select(price => new ModelPrice
        {
            ProviderId = price.ProviderId,
            ModelId = price.ModelId,
            InputPricePerMillionTokens = price.InputPricePerMillionTokens,
            OutputPricePerMillionTokens = price.OutputPricePerMillionTokens
        })];

    /// <summary>
    /// 填充默认价格：仅当用户价格表为空或为 null 时生效，绝不覆盖用户已维护的数据。
    /// </summary>
    public static List<ModelPrice> FillIfEmpty(List<ModelPrice>? existing)
    {
        if (existing is { Count: > 0 })
        {
            return existing;
        }

        return CreateDefaults();
    }
}
