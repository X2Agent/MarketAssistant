using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Archive;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Analysis;

/// <summary>
/// AI 评级变化告警：报告归档后与同一标的的上一次评级比对，发生变化时经统一告警中心上报。
/// 评级下调（向卖出方向移动）为 Warning，上调为 Info——用户对"变坏"需要更强的注意力。
/// </summary>
public sealed class AnalysisRatingAlertService : IDisposable
{
    /// <summary>取上一次评级所需的报告条数（最新一条 + 上一条）。</summary>
    private const int PreviousReportLookup = 2;

    private readonly ReportArchiveService _archiveService;
    private readonly IUserSettingService _userSettingService;
    private readonly IAlertCenterService _alertCenterService;
    private readonly ILogger<AnalysisRatingAlertService> _logger;

    public AnalysisRatingAlertService(
        ReportArchiveService archiveService,
        IUserSettingService userSettingService,
        IAlertCenterService alertCenterService,
        ILogger<AnalysisRatingAlertService> logger)
    {
        _archiveService = archiveService;
        _userSettingService = userSettingService;
        _alertCenterService = alertCenterService;
        _logger = logger;

        _archiveService.ReportSaved += OnReportSaved;
    }

    private void OnReportSaved(MarketAnalysisReport report)
    {
        // fire-and-forget：归档主链不得被告警消费阻塞；异常在 HandleRatingChangeAsync 内隔离
        _ = HandleRatingChangeAsync(report);
    }

    private async Task HandleRatingChangeAsync(MarketAnalysisReport report)
    {
        try
        {
            if (!_userSettingService.CurrentSetting.EnableRatingChangeAlert)
                return;

            // 历史报告没有市场信息，无法确定归属市场，跳过（读取方也不得从代码推断市场）
            if (report.MarketType is not { } marketType)
                return;

            var currentRating = report.CoordinatorResult.InvestmentRating;
            var summaries = await _archiveService
                .GetSummariesAsync(report.AssetSymbol, PreviousReportLookup, marketType: marketType)
                .ConfigureAwait(false);

            // 不足两条说明这是该标的的首份报告，没有可比较的基准
            if (summaries.Count < PreviousReportLookup)
                return;

            var previousRating = summaries[1].Rating;
            if (previousRating is not { } previous || previous == currentRating)
                return;

            // 评级枚举按看多→看空排列，数值变大即评级下调
            var isDowngrade = currentRating > previous;
            await _alertCenterService.RaiseAlertAsync(new AlertEvent
            {
                MarketType = marketType,
                Symbol = report.AssetSymbol,
                Level = isDowngrade ? AlertLevel.Warning : AlertLevel.Info,
                Source = AlertSource.Signal,
                Title = "AI 评级变化",
                Content = $"{report.AssetSymbol} 的综合评级由 {Describe(previous)} 变为 {Describe(currentRating)}"
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "处理 AI 评级变化告警失败: {Asset}", report.AssetSymbol);
        }
    }

    private static string Describe(InvestmentRating rating) => rating switch
    {
        InvestmentRating.StrongBuy => "强烈买入",
        InvestmentRating.Buy => "买入",
        InvestmentRating.Hold => "持有",
        InvestmentRating.Reduce => "减持",
        InvestmentRating.Sell => "卖出",
        InvestmentRating.StrongSell => "强烈卖出",
        _ => rating.ToString()
    };

    public void Dispose()
    {
        _archiveService.ReportSaved -= OnReportSaved;
        GC.SuppressFinalize(this);
    }
}
