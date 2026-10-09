using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Models.AShare;
using MarketAssistant.DataProviders.AShare;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Tools.AShare;

/// <summary>
/// A股市场情绪工具实现
/// </summary>
public sealed class AShareSentimentTools(
    ZhiTuMarketClient zhiTuClient,
    IUserSettingService userSettingService,
    ILogger<AShareSentimentTools> logger) : ISentimentTools
{
    private readonly ZhiTuMarketClient _zhiTuClient = zhiTuClient ?? throw new ArgumentNullException(nameof(zhiTuClient));
    private readonly IUserSettingService _userSettingService = userSettingService ?? throw new ArgumentNullException(nameof(userSettingService));
    private readonly ILogger<AShareSentimentTools> _logger = logger;

    [Description("根据股票代码获取资金流向数据")]
    public async Task<FundFlow> GetFundFlowAsync([Description("股票代码")] string assetSymbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var stockCode = new string([.. assetSymbol.Where(char.IsDigit)]);
            var token = _userSettingService.CurrentSetting.ZhiTuApiToken;

            var dailyFlows = await _zhiTuClient.GetTransactionHistoryAsync<ZhiTuFundFlowData>(stockCode, token, cancellationToken);

            if (dailyFlows.Count == 0)
                throw new FriendlyException($"获取资金流向数据为空: {assetSymbol}");

            return MapToFundFlow(dailyFlows);
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "获取资金流向失败: {Symbol}", assetSymbol);
            throw new FriendlyException($"获取资金流向数据时发生错误: {ex.Message}", ex);
        }
    }

    private static FundFlow MapToFundFlow(List<ZhiTuFundFlowData> dailyFlows)
    {
        // ZhiTu API 返回顺序不保证，按日期降序排列确保 [0] 为最新交易日
        dailyFlows.Sort((a, b) => b.T.CompareTo(a.T));

        var latest = dailyFlows[0];

        // 资金流向标准定义：净流入 = 主动买入成交额 − 主动卖出成交额（缺失成分按 0 计入）
        var superDiff = OrZero(latest.ZmbtdCje) - OrZero(latest.ZmstdCje);
        var largeDiff = OrZero(latest.ZmbddCje) - OrZero(latest.ZmsddCje);
        var mediumDiff = OrZero(latest.ZmbzdCje) - OrZero(latest.ZmszdCje);
        var littleDiff = OrZero(latest.ZmbxdCje) - OrZero(latest.ZmsxdCje);

        // 主力 = 特大单 + 大单（成交额 ≥ 20万 或 成交量 ≥ 1000手）
        var mainIn = OrZero(latest.ZmbtdCje) + OrZero(latest.ZmbddCje);
        var mainOut = OrZero(latest.ZmstdCje) + OrZero(latest.ZmsddCje);
        var mainDiff = mainIn - mainOut;

        return new FundFlow
        {
            Date = latest.T,
            MainFundIn = mainIn,
            MainFundOut = mainOut,
            MainFundDiff = mainDiff,
            SuperFundDiff = superDiff,
            LargeFundDiff = largeDiff,
            MediumFundDiff = mediumDiff,
            LittleFundDiff = littleDiff,
            // 已按日期降序，Take(n) 即为最近 n 个交易日
            MainFund3 = CalcMainDiff(dailyFlows, 3),
            MainFund5 = CalcMainDiff(dailyFlows, 5),
            MainFund10 = CalcMainDiff(dailyFlows, 10),
            MainFund20 = CalcMainDiff(dailyFlows, 20)
        };
    }

    /// <summary>智兔对缺失的成交额返回 null，汇总时按 0 计入。</summary>
    private static decimal OrZero(decimal? value) => value ?? 0m;

    private static decimal CalcMainDiff(List<ZhiTuFundFlowData> flows, int days) =>
        flows.Take(days).Sum(d => OrZero(d.ZmbtdCje) + OrZero(d.ZmbddCje) - OrZero(d.ZmstdCje) - OrZero(d.ZmsddCje));

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetFundFlowAsync);
    }
}

/// <summary>
/// 智兔API资金流向数据 (/hs/history/transaction/{code})
/// <para>
/// 订单分级：特大单(成交额≥100万或量≥5000手) / 大单(≥20万/1000手) / 中单(≥4万/200手) / 小单(其余)
/// </para>
/// <para>
/// 命名规则：zmb = 主买(主动买入), zms = 主卖(主动卖出);
///          td = 特大单, dd = 大单, zd = 中单, xd = 小单;
///          cje = 成交额
/// </para>
/// </summary>
internal sealed class ZhiTuFundFlowData
{
    /// <summary>交易日期 (YYYYMMDD)</summary>
    [JsonPropertyName("t")] public int T { get; set; }

    // ── 主动买入(zmb)成交额 ──

    /// <summary>主买特大单成交额；智兔对缺失数据返回 null</summary>
    [JsonPropertyName("zmbtdcje")] public decimal? ZmbtdCje { get; set; }
    /// <summary>主买大单成交额；智兔对缺失数据返回 null</summary>
    [JsonPropertyName("zmbddcje")] public decimal? ZmbddCje { get; set; }
    /// <summary>主买中单成交额；智兔对缺失数据返回 null</summary>
    [JsonPropertyName("zmbzdcje")] public decimal? ZmbzdCje { get; set; }
    /// <summary>主买小单成交额；智兔对缺失数据返回 null</summary>
    [JsonPropertyName("zmbxdcje")] public decimal? ZmbxdCje { get; set; }

    // ── 主动卖出(zms)成交额 ──

    /// <summary>主卖特大单成交额；智兔对缺失数据返回 null</summary>
    [JsonPropertyName("zmstdcje")] public decimal? ZmstdCje { get; set; }
    /// <summary>主卖大单成交额；智兔对缺失数据返回 null</summary>
    [JsonPropertyName("zmsddcje")] public decimal? ZmsddCje { get; set; }
    /// <summary>主卖中单成交额；智兔对缺失数据返回 null</summary>
    [JsonPropertyName("zmszdcje")] public decimal? ZmszdCje { get; set; }
    /// <summary>主卖小单成交额；智兔对缺失数据返回 null</summary>
    [JsonPropertyName("zmsxdcje")] public decimal? ZmsxdCje { get; set; }
}
