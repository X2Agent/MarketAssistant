using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.Briefing;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace MarketAssistant.ViewModels.Home;

/// <summary>
/// 每日简报 ViewModel：进入首页先读当天缓存（命中即可呈现，满足"启动 10 秒内可读"），
/// 未命中时后台生成一次；自选与要闻均为空时展示引导空态。
/// </summary>
public partial class DailyBriefingViewModel : ViewModelBase, IDisposable
{
    private readonly MarketContext _marketContext;
    private bool _disposed;

    private IDailyBriefingService BriefingService =>
        _marketContext.GetService<IDailyBriefingService>();

    [ObservableProperty]
    private string _briefingContent = string.Empty;

    [ObservableProperty]
    private bool _hasBriefing;

    [ObservableProperty]
    private bool _isGenerating;

    /// <summary>简报元信息（生成时间 / 纳入自选数）。</summary>
    [ObservableProperty]
    private string _briefingMetaText = string.Empty;

    /// <summary>
    /// 是否展示简报卡片：有简报、生成中、失败或引导空态时均展示，
    /// 尚未开始加载（Idle 且无生成动作）时隐藏，避免首页闪现空卡。
    /// </summary>
    public bool ShowCard => HasBriefing || IsGenerating || State != LoadState.Idle;

    public DailyBriefingViewModel(
        MarketContext marketContext,
        ILogger<DailyBriefingViewModel> logger)
        : base(logger)
    {
        _marketContext = marketContext;
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
        await GenerateAsync(force: true);
    }

    /// <summary>失败态重试：重新生成当天简报。</summary>
    [RelayCommand]
    private async Task RetryAsync()
    {
        await GenerateAsync(force: true);
    }

    /// <summary>
    /// 读取当天缓存；未命中时后台自动生成一次（当天首次进入首页的自动路径，
    /// 之后各次均命中缓存，不重复消耗 Token）。
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
                return;
            }

            await GenerateAsync(force: false);
        }, "加载每日简报");
    }

    private async Task GenerateAsync(bool force)
    {
        if (_disposed || IsGenerating)
            return;

        IsGenerating = true;
        try
        {
            var briefing = await BriefingService.GenerateAsync(force);
            if (_disposed)
                return;

            ApplyBriefing(briefing);
            if (briefing == null)
            {
                // 无自选无要闻：引导态，不是错误
                SetEmpty();
            }
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

    private void ApplyBriefing(DailyBriefing? briefing)
    {
        HasBriefing = briefing != null;
        BriefingContent = briefing?.Content ?? string.Empty;
        BriefingMetaText = briefing is null
            ? string.Empty
            : $"生成于 {briefing.GeneratedAtText}"
              + (briefing.FavoriteCount > 0 ? $" · 纳入自选 {briefing.FavoriteCount} 只" : string.Empty);

        if (briefing != null)
        {
            SetReady();
        }
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
    }
}
