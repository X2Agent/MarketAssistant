using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.InvestmentSelection.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace MarketAssistant.ViewModels;

/// <summary>
/// AI 选股页 - 运行部分：三个入口（用户需求 / 新闻 / 快速策略）共用的运行流程、三步进度、计时、取消与失败重试。
/// 与主视图模型拆分为 partial 类，避免单文件继续膨胀。
/// </summary>
public partial class AssetSelectionPageViewModel
{
    private readonly DispatcherTimer _elapsedTimer;

    private CancellationTokenSource? _runCts;
    private SelectionRun? _lastRun;
    private long _runStartedTimestamp;

    /// <summary>一次可重试的选股运行：展示用的查询摘要、首步标题与执行体。</summary>
    private sealed record SelectionRun(
        string QueryDescription,
        string FirstStepTitle,
        Func<IProgress<InvestmentSelectionProgress>, CancellationToken, Task<InvestmentSelectionResult>> ExecuteAsync);

    /// <summary>本次运行的查询摘要，运行态与结果头部共用，让用户确认“AI 在处理什么”。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultSubtitle))]
    private string _queryDescription = string.Empty;

    /// <summary>运行态的三步进度；每次运行整体替换，进度回调只改自己那一轮的步骤对象。</summary>
    [ObservableProperty]
    private SelectionStepItem[] _steps = [];

    [ObservableProperty]
    private string _elapsedText = "00:00";

    /// <summary>失败态提示；非空时输入态顶部显示失败条与「重试」。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFailure))]
    private string _failureMessage = string.Empty;

    public bool HasFailure => FailureMessage.Length > 0;

    /// <summary>运行态进度卡标题，如“AI 正在为您选股”。</summary>
    public string RunningTitle => $"AI 正在为您{SelectionNoun}";

    [RelayCommand]
    private Task ExecuteAnalysisAsync() => SelectedMode?.ModeType switch
    {
        SelectionModeType.UserRequirement => ExecuteSelectionAsync(),
        SelectionModeType.NewsAnalysis => ExecuteNewsSelectionAsync(),
        _ => Task.CompletedTask
    };

    [RelayCommand]
    private async Task ExecuteQuickSelectionAsync(QuickSelectionStrategyInfo? strategy)
    {
        if (strategy == null)
            return;

        var marketType = _marketContext.CurrentMarket;
        await RunSelectionAsync(new SelectionRun(
            $"快速策略 · {strategy.Name}",
            "解析策略，生成筛选条件",
            (progress, cancellationToken) => _investmentSelectionService.QuickSelectAsync(
                strategy.Strategy, marketType, progress, cancellationToken)));
    }

    [RelayCommand]
    private void CancelSelection() => _runCts?.Cancel();

    [RelayCommand]
    private async Task RetrySelectionAsync()
    {
        if (_lastRun != null)
        {
            await RunSelectionAsync(_lastRun);
        }
    }

    private async Task ExecuteSelectionAsync()
    {
        if (string.IsNullOrWhiteSpace(UserRequirements))
        {
            ShowValidationMessage(_marketContext.CurrentMarket == MarketType.Crypto
                ? "请输入您的选币需求，例如：寻找市值前50、24h涨幅超过5%、交易量活跃的虚拟币"
                : "请输入您的选股需求，例如：寻找市值在100-500亿之间，PE低于20倍的价值股");
            return;
        }

        ClearValidationMessage();
        var request = new InvestmentRecommendationRequest
        {
            MarketType = _marketContext.CurrentMarket,
            UserRequirements = UserRequirements
        };

        await RunSelectionAsync(new SelectionRun(
            $"用户需求 · {FlattenWhitespace(UserRequirements)}",
            "理解需求，生成筛选条件",
            (progress, cancellationToken) => _investmentSelectionService.RecommendByUserRequirementAsync(
                request, progress, cancellationToken)));
    }

    private async Task ExecuteNewsSelectionAsync()
    {
        if (string.IsNullOrWhiteSpace(NewsContent))
        {
            ShowValidationMessage(_marketContext.CurrentMarket == MarketType.Crypto
                ? "请输入新闻内容或热点信息，例如：交易所上线新币、链上活跃地址创新高等"
                : "请输入新闻内容或热点信息，例如：央行降准利好银行股，新能源汽车销量创新高等");
            return;
        }

        ClearValidationMessage();
        var request = new NewsBasedInvestmentRequest
        {
            MarketType = _marketContext.CurrentMarket,
            NewsContent = NewsContent,
            MaxRecommendations = 5
        };

        await RunSelectionAsync(new SelectionRun(
            $"新闻内容 · {FlattenWhitespace(NewsContent)}",
            "解读新闻，生成筛选条件",
            (progress, cancellationToken) => _investmentSelectionService.RecommendByNewsAsync(
                request, progress, cancellationToken)));
    }

    /// <summary>
    /// 三个入口共用的运行流程：切到运行态 → 执行并接收三步进度 → 成功进入结果态；
    /// 取消静默回到输入态，失败回到输入态并显示失败条。
    /// </summary>
    private async Task RunSelectionAsync(SelectionRun run)
    {
        // 快速策略卡在界面切换前可被连点，同一时刻只允许一次运行（每次运行都消耗 Token）
        if (IsBusy)
            return;

        _lastRun = run;
        var cancellationToken = ResetCancellationToken(ref _runCts);

        QueryDescription = run.QueryDescription;
        FailureMessage = string.Empty;
        SelectionResult = null;

        SelectionStepItem[] steps =
        [
            new(InvestmentSelectionStep.GenerateCriteria, run.FirstStepTitle) { State = SelectionStepState.Running },
            new(InvestmentSelectionStep.ScreenTargets, "筛选候选标的"),
            new(InvestmentSelectionStep.AnalyzeAssets, "AI 分析并生成推荐")
        ];
        Steps = steps;

        ElapsedText = FormatElapsed(TimeSpan.Zero);
        _runStartedTimestamp = Stopwatch.GetTimestamp();
        _elapsedTimer.Start();
        IsBusy = true;

        try
        {
            var progress = new Progress<InvestmentSelectionProgress>(update => ApplyProgress(steps, update));
            var result = await run.ExecuteAsync(progress, cancellationToken);

            // 取消与完成同时发生时以取消为准，丢弃结果
            cancellationToken.ThrowIfCancellationRequested();

            _generatedAt = DateTime.Now;
            SelectionResult = result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Logger?.LogInformation("AI{Noun}已取消", SelectionNoun);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "执行 AI{Noun} 失败", SelectionNoun);
            FailureMessage = ErrorMessageMapper.GetUserFriendlyMessageWithContext(ex, $"AI{SelectionNoun}");
        }
        finally
        {
            _elapsedTimer.Stop();
            IsBusy = false;
        }
    }

    private static void ApplyProgress(SelectionStepItem[] steps, InvestmentSelectionProgress update)
    {
        var step = steps.First(item => item.Step == update.Step);
        step.State = update.Status == InvestmentSelectionStepStatus.Running
            ? SelectionStepState.Running
            : SelectionStepState.Completed;
        step.Detail = update.Detail;
    }

    private void OnElapsedTimerTick(object? sender, EventArgs e) =>
        ElapsedText = FormatElapsed(Stopwatch.GetElapsedTime(_runStartedTimestamp));

    private static string FormatElapsed(TimeSpan elapsed) =>
        $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";

    /// <summary>把多行输入折成单行摘要，供运行态与结果头部单行截断展示。</summary>
    private static string FlattenWhitespace(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
