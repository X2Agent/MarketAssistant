using MarketAssistant.Applications.Charts;
using MarketAssistant.Applications.Charts.Models;
using MarketAssistant.Applications.Technical;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.PriceAlert;

/// <summary>
/// 指标类告警规则评估器：按周期为启用的指标规则（量比 / MACD / RSI / KDJ / BOLL / MA）
/// 计算度量快照并驱动规则状态机。指标依赖 K 线、无法逐 tick 评估，因此与价格规则分离成独立链路。
/// </summary>
/// <remarks>
/// 取数失败（K 线接口异常/样本不足）按符号隔离并记录，不得让整个评估周期中断，
/// 也不得把失败伪装成"条件未满足"——度量为 null 时该符号本轮直接跳过。
/// </remarks>
public sealed class IndicatorAlertEvaluator : IDisposable
{
    /// <summary>评估周期。指标条件不需要 tick 级灵敏度，5 分钟是时效与取数成本的平衡点。</summary>
    public static readonly TimeSpan EvaluationInterval = TimeSpan.FromMinutes(5);

    /// <summary>启动后首次评估的延迟：等 K 线服务与规则加载就绪，避免应用启动即打满外部接口。</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    /// <summary>每次评估拉取的 K 线根数（覆盖 MACD 26 根预热窗口 + 量比 20 根均量样本）。</summary>
    private const int BarCount = 120;

    private readonly PriceAlertService _alertService;
    private readonly AlertRuleSymbolResolver _symbolResolver;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IndicatorAlertEvaluator> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loopTask;

    public IndicatorAlertEvaluator(
        PriceAlertService alertService,
        AlertRuleSymbolResolver symbolResolver,
        IServiceProvider serviceProvider,
        ILogger<IndicatorAlertEvaluator> logger)
    {
        _alertService = alertService;
        _symbolResolver = symbolResolver;
        _serviceProvider = serviceProvider;
        _logger = logger;

        _loopTask = Task.Run(() => EvaluateLoopAsync(_cts.Token));
    }

    private async Task EvaluateLoopAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(StartupDelay, ct).ConfigureAwait(false);
            await EvaluateAllRulesAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "指标告警首次评估异常，将在下个周期重试");
        }

        using var timer = new PeriodicTimer(EvaluationInterval);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            try
            {
                await EvaluateAllRulesAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "指标告警评估周期异常");
            }
        }
    }

    internal async Task EvaluateAllRulesAsync(CancellationToken ct)
    {
        var rules = _alertService.Rules
            .Where(rule => rule.Enabled && rule.IsIndicatorCondition)
            .ToList();
        if (rules.Count == 0)
            return;

        // 同一周期内按 (市场, 符号, K线周期) 缓存度量：多条规则常盯同一标的，避免重复取数
        var metricCache =
            new Dictionary<(MarketType Market, string Symbol, KLineType Period), AlertRuleMetric?>();

        foreach (var rule in rules)
        {
            ct.ThrowIfCancellationRequested();

            IReadOnlyList<string> symbols;
            try
            {
                symbols = await _symbolResolver.ResolveSymbolsAsync(rule, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 自选股查询失败按规则隔离：跳过该规则本轮，不影响其它规则
                _logger.LogWarning(ex, "解析指标告警规则符号失败，本轮跳过: {RuleId}", rule.Id);
                continue;
            }

            foreach (var symbol in symbols)
            {
                var key = (rule.MarketType, symbol, rule.KLineType);
                if (!metricCache.TryGetValue(key, out var metric))
                {
                    metric = await CalculateMetricAsync(rule.MarketType, symbol, rule.KLineType, ct)
                        .ConfigureAwait(false);
                    metricCache[key] = metric;
                }

                if (metric is null)
                    continue;

                await _alertService.EvaluateIndicatorRuleAsync(rule, symbol, metric, ct).ConfigureAwait(false);
            }

            _alertService.CompleteIndicatorRuleEvaluation(rule, symbols);
        }
    }

    private async Task<AlertRuleMetric?> CalculateMetricAsync(
        MarketType market, string symbol, KLineType kLineType, CancellationToken ct)
    {
        try
        {
            var kLineService = _serviceProvider.GetRequiredKeyedService<IKLineService>(market);
            var indicatorService = _serviceProvider.GetRequiredKeyedService<ITechnicalIndicatorService>(market);

            var kLines = await kLineService.GetKLineDataAsync(symbol, kLineType, BarCount)
                .ConfigureAwait(false);
            if (kLines is null || kLines.Count == 0)
            {
                _logger.LogWarning("指标告警取数为空: {Market} {Symbol} {KLineType}", market, symbol, kLineType);
                return null;
            }

            // 指标公式统一由 TechnicalIndicatorService 计算，本类只做装配
            return IndicatorMetricCalculator.Calculate(kLines, indicatorService.CalculateFrom(kLines));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "计算指标告警度量失败: {Market} {Symbol} {KLineType}", market, symbol, kLineType);
            return null;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _loopTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "指标告警评估器停止异常（已忽略）");
        }

        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
