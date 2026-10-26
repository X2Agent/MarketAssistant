namespace MarketAssistant.ViewModels;

/// <summary>
/// 页面数据加载状态，用于统一区分「加载中 / 有数据 / 无数据 / 失败」四种呈现。
/// </summary>
public enum LoadState
{
    /// <summary>尚未加载（或加载被用户取消）。</summary>
    Idle,

    /// <summary>正在加载。</summary>
    Loading,

    /// <summary>加载成功且有数据。</summary>
    Ready,

    /// <summary>加载成功但数据源返回空结果。</summary>
    Empty,

    /// <summary>加载失败（异常或数据源不可用）。</summary>
    Failed
}
