using MarketAssistant.Applications.Charts.Models;
using MarketAssistant.Infrastructure;
using MarketAssistant.Services.Data;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json.Nodes;
using static MarketAssistant.Infrastructure.Core.CryptoSymbolConverter;

namespace MarketAssistant.Applications.Charts;

/// <summary>
/// 虚拟币K线数据服务实现，委托 BinanceMarketDataService 获取原始数据
/// </summary>
public class CryptoKLineService : IKLineService
{
    private readonly BinanceMarketDataService _binanceService;
    private readonly ILogger<CryptoKLineService> _logger;

    public CryptoKLineService(
        BinanceMarketDataService binanceService,
        ILogger<CryptoKLineService> logger)
    {
        _binanceService = binanceService ?? throw new ArgumentNullException(nameof(binanceService));
        _logger = logger;
    }

    /// <summary>
    /// 获取K线数据
    /// </summary>
    public async Task<List<KLineData>> GetKLineDataAsync(string code, KLineType kLineType, int count = 250)
    {
        var interval = kLineType switch
        {
            KLineType.Daily => "1d",
            KLineType.Weekly => "1w",
            KLineType.Monthly => "1M",
            KLineType.Minute5 => "5m",
            KLineType.Minute15 => "15m",
            _ => "1d"
        };

        var formattedSymbol = ToBinanceFormat(code);
        var limit = Math.Min(count, 1000);

        _logger.LogInformation("获取币安K线: {Symbol}, 周期: {Interval}, 数量: {Limit}", formattedSymbol, interval, limit);

        var jsonArray = await _binanceService.GetKlinesAsync(formattedSymbol, interval, limit);

        if (jsonArray == null || jsonArray.Count == 0)
        {
            _logger.LogWarning("币安API返回K线数据为空: {Symbol}", formattedSymbol);
            return [];
        }

        var result = ParseKlineData(jsonArray);
        CalculatePriceChanges(result);

        _logger.LogInformation("成功获取K线数据: {Symbol}, {Count} 条", formattedSymbol, result.Count);
        return result;
    }

    private static List<KLineData> ParseKlineData(JsonArray jsonArray)
    {
        var result = new List<KLineData>(jsonArray.Count);

        foreach (var item in jsonArray)
        {
            if (item is not JsonArray arr || arr.Count < 11) continue;

            result.Add(new KLineData
            {
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(arr[0]!.GetValue<long>()).DateTime,
                Open = decimal.Parse(arr[1]!.GetValue<string>(), CultureInfo.InvariantCulture),
                High = decimal.Parse(arr[2]!.GetValue<string>(), CultureInfo.InvariantCulture),
                Low = decimal.Parse(arr[3]!.GetValue<string>(), CultureInfo.InvariantCulture),
                Close = decimal.Parse(arr[4]!.GetValue<string>(), CultureInfo.InvariantCulture),
                Volume = decimal.Parse(arr[5]!.GetValue<string>(), CultureInfo.InvariantCulture),
                Amount = decimal.Parse(arr[7]!.GetValue<string>(), CultureInfo.InvariantCulture)
            });
        }

        return result;
    }

    private static void CalculatePriceChanges(List<KLineData> klineDataList)
    {
        for (int i = 0; i < klineDataList.Count; i++)
        {
            if (i == 0)
            {
                klineDataList[i].PreClose = klineDataList[i].Open;
                klineDataList[i].Change = 0;
                klineDataList[i].PctChg = 0;
            }
            else
            {
                var preClose = klineDataList[i - 1].Close;
                klineDataList[i].PreClose = preClose;

                if (preClose > 0)
                {
                    klineDataList[i].Change = klineDataList[i].Close - preClose;
                    klineDataList[i].PctChg = (klineDataList[i].Change / preClose) * 100;
                }
                else
                {
                    klineDataList[i].Change = 0;
                    klineDataList[i].PctChg = 0;
                }
            }
        }
    }
}
