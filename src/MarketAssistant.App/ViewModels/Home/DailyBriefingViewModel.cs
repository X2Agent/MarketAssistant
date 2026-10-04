using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Agents.TokenManagement;
using MarketAssistant.Applications.Briefing;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Cost;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace MarketAssistant.ViewModels.Home;

/// <summary>
/// 每日简报 ViewModel：进入首页先读当天缓存（命中即可呈现，满足"启动 10 秒内可读"），
/// 未命中时后台生成一次；自选与要闻均为空时展示引导空态。
/// </summary>
public partial class DailyBriefingViewModel : ViewModelBase, IDisposable
{
    /// <summary>累计用量摘要的回溯窗口（天）。</summary>
    private const int UsageSummaryWindowDays = 30;

    private readonly MarketContext _marketContext;
    private readonly IAnalysisCostService _costService;
    private readonly IUserSettingService _userSettingService;
    private bool _disposed;

    private IDailyBriefingService BriefingService =>
        _marketContext.GetService<IDailyBriefingService>();

    [ObservableProperty]
    private string _briefingContent = string.Empty;

    [ObservableProperty]
    private bool _hasBriefing;

    [ObservableProperty]
    private bool _isGenerating;

    /// <summary>简报元信息（生成时间 / 纳入自选数 / 本次 Token 与成本）。</summary>
    [ObservableProperty]
    private string _briefingMetaText = string.Empty;

    /// <summary>
    /// 近 30 天累计用量摘要（如"近 30 天 12 次 · 约 82.4k tokens"）。
    /// 无历史记录时为空串，UI 直接隐藏。
    /// </summary>
    [ObservableProperty]
    private string _usageSummaryText = string.Empty;

    /// <summary>
    /// 是否展示简报卡片：有简报、生成中、失败或引导空态时均展示，
    /// 尚未开始加载（Idle 且无生成动作）时隐藏，避免首页闪现空卡。
    /// </summary>
    public bool ShowCard => HasBriefing || IsGenerating || State != LoadState.Idle;

    /// <summary>累计摘要是否有内容可展示。</summary>
    public bool ShowUsageSummary => !string.IsNullOrEmpty(UsageSummaryText);

    public DailyBriefingViewModel(
        MarketContext marketContext,
        IAnalysisCostService costService,
        IUserSettingService userSettingService,
        ILogger<DailyBriefingViewModel> logger)
        : base(logger)
    {
        _marketContext = marketContext;
        _costService = costService;
        _userSettingService = userSettingService;
        EmptyMessage = "添加自选资产后，每天打开将自动生成隔夜要闻与异动归因简报";
        SetRetryCommand(() => RetryAsync());
        SubscribeToMarketChanges(_marketContext);
        _ = LoadCachedAsync();
    }

    protected override void OnMarketChanged(MarketType newMarket)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
                return;

            _ = LoadCachedAsync();
        });
    }

    /// <summary>手动刷新：强制重新生成当天简报。</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        await GenerateAsync(force: true, manual: true);
    }

    /// <summary>失败态重试：重新生成当天简报。</summary>
    [RelayCommand]
    private async Task RetryAsync()
    {
        await GenerateAsync(force: true, manual: true);
    }

    /// <summary>
    /// 读取当天缓存；未命中时在自动生成开关允许的前提下后台生成一次
    /// （当天首次进入首页的自动路径，之后各次均命中缓存，不重复消耗 Token）。
    /// </summary>
    private async Task LoadCachedAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            var cached = await BriefingService.GetTodayAsync();
            if (_disposed)
                return;

            if (cached != null)
            {
                ApplyBriefing(cached);
            }

            // 无论是否命中缓存都刷新累计摘要：即使用户关掉了自动生成，
            // 也应能看到"过去已经花了多少"，这是成本透明的一部分。
            await RefreshUsageSummaryAsync();

            if (cached != null)
            {
                return;
            }

            // 自动生成被关闭：不静默失败，明确告知用户原因与如何恢复。
            // 注意仍要展示卡片——否则用户会以为功能坏掉了。
            if (!BriefingService.IsAutoGenerateEnabled)
            {
                EmptyMessage = BriefingService.IsEnabled
                    ? "每日简报自动生成已关闭，可在设置中开启，或点击右上角刷新手动生成"
                    : "每日简报功能已关闭，可在设置中重新开启";
                SetEmpty();
                return;
            }

            await GenerateAsync(force: false, manual: false);
        }, "加载每日简报");
    }

    private async Task GenerateAsync(bool force, bool manual)
    {
        if (_disposed || IsGenerating)
            return;

        IsGenerating = true;
        try
        {
            var briefing = await BriefingService.GenerateAsync(force, manual);
            if (_disposed)
                return;

            ApplyBriefing(briefing);
            if (briefing == null)
            {
                // 无自选无要闻：引导态，不是错误
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
    /// Token 一律展示（这是简报唯一会产生的费用）；金额仅在价格表命中该模型时附加，
    /// 未定价时不显示金额，避免给出错误的"免费"暗示。
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

            var costText = _costService.FormatCostForModel(
                _userSettingService.CurrentSetting.ProviderId,
                briefing.ModelId,
                briefing.InputTokens,
                briefing.OutputTokens);

            parts.Add(string.IsNullOrEmpty(costText) ? tokenText : $"{tokenText} · {costText}");
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

        // ShowCard 依赖加载状态与生成状态，随同变化通知，避免卡片显隐滞后
        if (e.PropertyName is nameof(HasBriefing) or nameof(IsGenerating) or nameof(State))
        {
            OnPropertyChanged(nameof(ShowCard));
        }

        if (e.PropertyName == nameof(UsageSummaryText))
        {
            OnPropertyChanged(nameof(ShowUsageSummary));
        }
    }
}
