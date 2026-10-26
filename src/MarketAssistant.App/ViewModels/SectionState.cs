namespace MarketAssistant.ViewModels;

/// <summary>
/// 页面内单个数据区块的加载状态与提示文案。区块之间互不影响：
/// 某区块失败只降级自身，不会把整页推入失败态。
/// </summary>
public sealed record SectionState
{
    public LoadState State { get; init; } = LoadState.Idle;

    /// <summary>是否已有可展示数据。刷新期间保持为 true，避免骨架屏与内容区反复闪烁。</summary>
    public bool HasData { get; init; }

    public string EmptyText { get; init; } = "暂无数据";

    public string FailedText { get; init; } = "加载失败";

    public bool IsLoading => State == LoadState.Loading;

    public bool IsEmpty => State == LoadState.Empty;

    public bool IsFailed => State == LoadState.Failed;

    public bool IsReady => State == LoadState.Ready;

    /// <summary>仅首次加载（尚无数据）时展示骨架屏。</summary>
    public bool ShowSkeleton => IsLoading && !HasData;

    /// <summary>展示「空 / 失败」提示行。</summary>
    public bool ShowHint => IsEmpty || IsFailed;

    public string HintText => IsFailed ? FailedText : EmptyText;
}
