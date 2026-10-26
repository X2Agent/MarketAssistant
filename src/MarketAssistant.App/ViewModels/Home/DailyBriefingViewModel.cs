using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Agents.TokenManagement;
using MarketAssistant.Applications.Briefing;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace MarketAssistant.ViewModels.Home;

/// <summary>
/// 每日简报 ViewModel：进入首页与打开弹窗时只读当天缓存，纯手动策略下绝不自动生成；
/// 自选与要闻均为空时展示引导空态。弹窗由 <see cref="IWindowService"/> 统一打开。
/// </summary>
public partial class DailyBriefingViewModel : ViewModelBase, IDisposable
{
    /// <summary>累计用量摘要的回溯窗口（天）。</summary>
    private const int UsageSummaryWindowDays = 30;

    private readonly MarketContext _marketContext;
    private readonly IWindowService _windowService;
    private bool _disposed;

    private IDailyBriefingService BriefingService =>
        _marketContext.GetService<IDailyBriefingService>();

    [ObservableProperty]
    private string _briefingContent = string.Empty;

    [ObservableProperty]
    private bool _hasBriefing;

    [ObservableProperty]
    private bool _isGenerating;

    /// <summary>简报功能总开关的快照，用于禁用弹窗内的生成按钮。</summary>
    [ObservableProperty]
    private bool _isBriefingEnabled = true;

    /// <summary>简报元信息（生成时间 / 纳入自选数 / 本次 Token 与成本）。</summary>
    [ObservableProperty]
    private string _briefingMetaText = string.Empty;

    /// <summary>
    /// 近 30 天累计用量摘要（如"近 30 天 12 次 · 约 82.4k tokens"）。
    /// 无历史记录时为空串，UI 直接隐藏。
    /// </summary>
    [ObservableProperty]
    private string _usageSummaryText = string.Empty;

    /// <summary>累计摘要是否有内容可展示。</summary>
    public bool ShowUsageSummary => !string.IsNullOrEmpty(UsageSummaryText);

    /// <summary>
    /// 打开简报弹窗。窗口的创建、owner 选择与重复打开防护统一由窗口服务处理；
    /// 打开前先刷新一次缓存与开关快照，保证弹窗展示的是最新状态。
    /// </summary>
    [RelayCommand]
    private async Task OpenBriefingWindowAsync()
    {
        await LoadCachedAsync();
        _windowService.ShowDailyBriefingWindow(this);
    }

    public DailyBriefingViewModel(
        MarketContext marketContext,
        IWindowService windowService,
        ILogger<DailyBriefingViewModel> logger)
        : base(logger)
    {
        _marketContext = marketContext;
        _windowService = windowService;
        EmptyMessage = "今日简报尚未生成。生成会产生模型调用与费用，确认后点击「生成今日简报」。";
        SetRetryCommand(() => RetryAsync());
        SubscribeToMarketChanges(_marketContext);

        // 纯手动策略：此处只读取当天缓存，绝不自动触发生成。
        // 生成简报会产生真实费用，必须由用户在弹窗内显式确认，
        // 否则用户会在毫不知情的情况下被持续计费（且察觉不到）。
        _ = LoadCachedAsync();
    }

    protected override void OnMarketChanged(MarketType newMarket)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
                return;

            // 市场切换后缓存键随之改变，需重读；仍然只读缓存、不生成
            _ = LoadCachedAsync();
        });
    }

    /// <summary>生成今日简报（强制，覆盖当天缓存，会产生模型调用与费用）。</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        await GenerateAsync(force: true);
    }

    /// <summary>失败态重试：重新生成当天简报。</summary>
    [RelayCommand]
    private async Task RetryAsync()
    {
        await GenerateAsync(force: true);
    }

    /// <summary>
    /// 读取当天缓存与累计用量摘要。<b>不触发生成</b>。
    /// </summary>
    /// <remarks>
    /// 纯手动策略下，本方法没有任何"未命中就自动生成"的分支——
    /// 缓存未命中时保持空态，由用户在弹窗内点「生成今日简报」。
    /// 累计摘要仍无条件刷新：即使用户从未生成过，也应能看到历史花费，
    /// 这属于成本透明的一部分（展示在简报弹窗底部）。
    /// </remarks>
    private async Task LoadCachedAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            IsBriefingEnabled = BriefingService.IsEnabled;

            var cached = await BriefingService.GetTodayAsync();
            if (_disposed)
                return;

            if (cached != null)
            {
                ApplyBriefing(cached);
            }

            await RefreshUsageSummaryAsync();

            // 缓存未命中：进入空态，等用户显式点击「生成今日简报」。
            // 功能总开关关闭时需给出不同文案——两种情况用户的下一步动作不同。
            if (cached != null)
            {
                return;
            }

            EmptyMessage = BriefingService.IsEnabled
                ? "今日简报尚未生成。生成会产生模型调用与费用，确认后点击「生成今日简报」。"
                : "每日简报功能已关闭，可在设置中重新开启";
            SetEmpty();
        }, "加载每日简报");
    }

    private async Task GenerateAsync(bool force)
    {
        if (_disposed || IsGenerating)
            return;

        // 总开关关闭：不触发模型调用，也不清空已有简报，直接给出可操作的指引文案。
        // 弹窗生成按钮已按 IsBriefingEnabled 禁用，此处兜底设置变更未同步到 UI 的竞态。
        if (!BriefingService.IsEnabled)
        {
            IsBriefingEnabled = false;
            EmptyMessage = "每日简报功能已关闭，可在设置中重新开启";
            SetEmpty();
            return;
        }

        IsGenerating = true;
        try
        {
            var briefing = await BriefingService.GenerateAsync(force);
            if (_disposed)
                return;

            ApplyBriefing(briefing);
            if (briefing == null)
            {
                // 无自选无要闻：无可摘要内容，属空态而非错误
                EmptyMessage = "没有可摘要的内容：需先添加自选资产，或等待要闻数据到达";
                SetEmpty();
            }

            // 生成后累计值已变化，同步刷新摘要
            await RefreshUsageSummaryAsync();
        }
        catch (Exception ex)
        {
            if (_disposed)
                return;

            Logger?.LogWarning(ex, "生成每日简报失败");
            SetFailed(ErrorMessageMapper.GetUserFriendlyMessageWithContext(ex, "生成每日简报"));
            SetRetryCommand(() => RetryAsync());
        }
        finally
        {
            IsGenerating = false;
        }
    }

    /// <summary>
    /// 刷新近 30 天累计用量摘要。失败静默降级为空串——摘要只是补充信息，
    /// 不应让整张简报卡片进入失败态。
    /// </summary>
    private async Task RefreshUsageSummaryAsync()
    {
        try
        {
            var summary = await BriefingService.GetUsageSummaryAsync(UsageSummaryWindowDays);
            if (_disposed)
                return;

            if (!summary.HasValue)
            {
                UsageSummaryText = string.Empty;
                return;
            }

            var tokenPart = $"约 {TokenUsageFormatter.FormatCount(summary.TotalTokens)} tokens"
                            + (summary.Estimated ? "（含估算值）" : string.Empty);

            UsageSummaryText = $"近 {UsageSummaryWindowDays} 天 {summary.DayCount} 次 · {tokenPart}";
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "刷新简报累计用量摘要失败");
            UsageSummaryText = string.Empty;
        }
    }

    private void ApplyBriefing(DailyBriefing? briefing)
    {
        HasBriefing = briefing != null;
        BriefingContent = briefing?.Content ?? string.Empty;
        BriefingMetaText = briefing is null
            ? string.Empty
            : BuildMetaText(briefing);

        if (briefing != null)
        {
            SetReady();
        }
    }

    /// <summary>
    /// 元信息行：生成时间 + 纳入自选数 + Token 消耗。
    /// Token 一律展示（这是简报唯一会产生的费用）。
    /// </summary>
    private string BuildMetaText(DailyBriefing briefing)
    {
        var parts = new List<string> { $"生成于 {briefing.GeneratedAtText}" };

        if (briefing.FavoriteCount > 0)
        {
            parts.Add($"纳入自选 {briefing.FavoriteCount} 只");
        }

        if (briefing.HasTokenUsage)
        {
            var tokenText = $"消耗 {TokenUsageFormatter.FormatCount(briefing.InputTokens + briefing.OutputTokens)} tokens"
                            + (briefing.Estimated ? "（估算）" : string.Empty);

            parts.Add(tokenText);
        }

        return string.Join(" · ", parts);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        UnsubscribeFromMarketChanges(_marketContext);
        GC.SuppressFinalize(this);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(UsageSummaryText))
        {
            OnPropertyChanged(nameof(ShowUsageSummary));
        }
    }
}
