using MarketAssistant.Applications.Charts.Models;
using MarketAssistant.DataProviders;
using MarketAssistant.DataProviders.AShare;
using MarketAssistant.Infrastructure;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarketAssistant.Applications.Charts;

/// <summary>
/// A股K线数据服务实现
/// </summary>
/// <remarks>
/// 数据源级联：智兔优先（股票侧数据更全：成交额/昨收/复权类型）；
/// 智兔不覆盖基金/ETF（实测 404 {"error":"not found"}）或异常/返回空时，
/// 回退新浪 K 线（对股票/ETF/LOF/货币基金均覆盖，实测稳定无限流）。
/// 双源均无数据时抛出面向用户的友好异常，原始错误进日志。
/// </remarks>
public class AShareKLineService(
    ZhiTuMarketClient zhiTuClient,
    SinaKLineClient sinaKLineClient,
    ILogger<AShareKLineService> logger,
    IUserSettingService userSettingService) : IKLineService
{
    private readonly ZhiTuMarketClient _zhiTuClient = zhiTuClient ?? throw new ArgumentNullException(nameof(zhiTuClient));
    private readonly SinaKLineClient _sinaKLineClient = sinaKLineClient ?? throw new ArgumentNullException(nameof(sinaKLineClient));
    private readonly IUserSettingService _userSettingService = userSettingService;
    private readonly ILogger<AShareKLineService> _logger = logger;

    /// <summary>
    /// 获取K线数据（统一入口，根据类型调用对应的实现）
    /// </summary>
    public async Task<List<KLineData>> GetKLineDataAsync(string code, KLineType kLineType, int count = 250)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new FriendlyException("资产代码不能为空，无法获取K线数据");
        }

        // 根据K线类型映射到智图API的时间周期参数，并同步映射新浪 K 线的 scale（分钟数）
        var (interval, dataTypeName, sinaScale) = kLineType switch
        {
            KLineType.Daily => ("d", "日K线", 240),
            KLineType.Weekly => ("w", "周K线", 1200),
            KLineType.Monthly => ("m", "月K线", 7200),
            KLineType.Minute5 => ("5", "5分钟K线", 5),
            KLineType.Minute15 => ("15", "15分钟K线", 15),
            _ => ("d", "日K线", 240) // 默认日K线
        };

        try
        {
            var dataset = await GetKLineDataInternalAsync(code, interval, dataTypeName, count: count);
            if (dataset.Data.Count > 0)
            {
                return dataset.Data;
            }

            _logger.LogInformation("智兔未返回 {Code} 的{DataType}，回退新浪 K 线数据源", code, dataTypeName);
        }
        catch (Exception ex)
        {
            // 智兔不覆盖基金/ETF（404）或令牌/网络异常时静默回退，原始错误进日志
            _logger.LogInformation(ex, "智兔获取 {Code} 的{DataType}失败，回退新浪 K 线数据源", code, dataTypeName);
        }

        var sinaData = await GetSinaKLineDataAsync(code, sinaScale, count);
        if (sinaData.Count > 0)
        {
            return sinaData;
        }

        throw new FriendlyException($"获取{dataTypeName}失败：数据源暂不可用，请稍后重试");
    }

    /// <summary>
    /// 新浪 K 线兜底：映射为市场无关的 <see cref="KLineData"/>。
    /// 新浪不提供昨收，用前一根收盘价派生，保证涨跌额/涨跌幅与主图口径一致。
    /// </summary>
    private async Task<List<KLineData>> GetSinaKLineDataAsync(string code, int scale, int count)
    {
        var sinaCode = StockSymbolConverter.ToClsFormat(code);
        var bars = await _sinaKLineClient.GetKLineBarsAsync(sinaCode, scale, count);

        var data = new List<KLineData>(bars.Count);
        foreach (var bar in bars)
        {
            if (!DateTime.TryParse(bar.Day, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
            {
                continue;
            }

            data.Add(new KLineData
            {
                Timestamp = timestamp,
                Open = bar.Open,
                High = bar.High,
                Low = bar.Low,
                Close = bar.Close,
                Volume = bar.Volume,
                // 分钟线响应含成交额，日/周/月不含；缺失记 0 而非伪造
                Amount = bar.Amount ?? 0m
            });
        }

        data.Sort(static (left, right) => left.Timestamp.CompareTo(right.Timestamp));

        for (var i = 0; i < data.Count; i++)
        {
            var preClose = i > 0 ? data[i - 1].Close : 0m;
            data[i].PreClose = preClose;

            if (preClose > 0m)
            {
                data[i].Change = data[i].Close - preClose;
                data[i].PctChg = data[i].Change / preClose * 100m;
            }
        }

        return data;
    }

    #region 内部实现方法

    private async Task<KLineDataSet> GetKLineDataInternalAsync(
        string symbol,
        string interval,
        string dataType,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string adjustType = "n",
        int? count = null)
    {
        try
        {
            // 验证参数
            if (string.IsNullOrEmpty(symbol))
            {
                throw new ArgumentException("股票代码参数必须提供");
            }

            // 处理股票代码格式
            string formattedSymbol = StockSymbolConverter.ToZhiTuFormat(symbol);

            // 构建API URL
            string apiUrl = BuildZhiTuApiUrl(formattedSymbol, interval, adjustType, startDate, endDate);

            // 发送请求并获取数据
            var zhiTuData = await FetchZhiTuDataAsync(apiUrl, dataType, symbol);

            // 转换为应用程序数据模型
            var klineDataSet = new KLineDataSet
            {
                Symbol = symbol,
                Interval = interval,
                Data = []
            };

            // 解析数据
            ParseZhiTuKLineData(zhiTuData, klineDataSet);

            // 按调用方请求的 count 截取最近的 N 条（已按时间升序排序，取最后 count 条）
            if (count.HasValue && count.Value > 0 && klineDataSet.Data.Count > count.Value)
            {
                klineDataSet.Data = [.. klineDataSet.Data.Skip(klineDataSet.Data.Count - count.Value)];
            }

            return klineDataSet;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取{DataType}数据时发生错误 - 股票代码: {Symbol}", dataType, symbol);
            throw new FriendlyException($"获取{dataType}数据失败: {ex.Message}", ex);
        }
    }

    private string BuildZhiTuApiUrl(string symbol, string interval, string adjustType = "n", DateTime? startDate = null, DateTime? endDate = null)
    {
        var token = _userSettingService.CurrentSetting.ZhiTuApiToken;
        var url = $"/hs/history/{symbol}/{interval}/{adjustType}?token={token}";

        // 如果没有指定时间范围，根据不同的interval设置合理的默认时间范围
        DateTime defaultStartDate;
        DateTime defaultEndDate = DateTime.Now;

        if (!startDate.HasValue && !endDate.HasValue)
        {
            defaultStartDate = interval.ToLowerInvariant() switch
            {
                // 日K线，默认查询最近1年
                "d" => DateTime.Now.AddYears(-1),
                // 周K线，默认查询最近3年
                "w" => DateTime.Now.AddYears(-3),
                // 月K线，默认查询最近10年
                "m" => DateTime.Now.AddYears(-10),
                // 年K线，默认查询最近10年
                "y" => DateTime.Now.AddYears(-10),
                "1" or "5" or "15" or "30" or "60" => DateTime.Now.AddDays(-60),
                // 其他情况，默认查询最近1年
                _ => DateTime.Now.AddYears(-1),
            };
            startDate = defaultStartDate;
            endDate = defaultEndDate;
        }

        if (startDate.HasValue)
        {
            url += $"&st={startDate.Value:yyyyMMdd}";
        }

        if (endDate.HasValue)
        {
            url += $"&et={endDate.Value:yyyyMMdd}";
        }

        return url;
    }

    private async Task<List<ZhiTuKLineData>> FetchZhiTuDataAsync(string url, string dataType, string symbol)
    {
        _logger.LogInformation("正在获取股票{DataType}数据: 股票代码: {Symbol}", dataType, symbol);

        try
        {
            // HTTP 访问与容错反序列化由 ZhiTuMarketClient 负责
            var zhiTuData = await _zhiTuClient.GetListAsync<ZhiTuKLineData>(url);

            if (zhiTuData.Count == 0)
            {
                throw new FriendlyException($"获取{dataType}数据失败: 返回数据为空");
            }

            return zhiTuData;
        }
        catch (FriendlyException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            throw new FriendlyException($"获取{dataType}数据失败: 网络请求错误 - {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new FriendlyException($"获取{dataType}数据失败: 数据解析错误 - {ex.Message}", ex);
        }
    }

    private void ParseZhiTuKLineData(List<ZhiTuKLineData> zhiTuData, KLineDataSet klineDataSet)
    {
        foreach (var item in zhiTuData)
        {
            // 解析时间戳
            if (DateTime.TryParse(item.T, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime timestamp))
            {
                // OHLC 为 null 的 bar 是上游缺失的无效数据，跳过；成交量/额缺失按 0 计
                if (item.O is not { } open || item.H is not { } high ||
                    item.L is not { } low || item.C is not { } close)
                    continue;

                var klineData = new KLineData
                {
                    Timestamp = timestamp,
                    Open = open,
                    High = high,
                    Low = low,
                    Close = close,
                    Volume = item.V ?? 0m,
                    Amount = item.A ?? 0m,
                    PreClose = item.Pc ?? 0m
                };

                // 计算涨跌额和涨跌幅（昨收缺失时无法计算，保持 0）
                if (item.Pc is { } preClose && preClose > 0)
                {
                    klineData.Change = close - preClose;
                    klineData.PctChg = (klineData.Change / preClose) * 100;
                }
                else
                {
                    klineData.Change = 0;
                    klineData.PctChg = 0;
                }

                klineDataSet.Data.Add(klineData);
            }
        }

        // 按日期时间排序（从旧到新）
        klineDataSet.Data = [.. klineDataSet.Data.OrderBy(x => x.Timestamp)];
    }

    #endregion
}

/// <summary>
/// ZhiTu API K线数据模型。
/// OHLC 缺失（null）的 bar 为无效数据，解析时跳过；成交量/额/昨收缺失按 0 计。
/// </summary>
[Serializable]
internal class ZhiTuKLineData
{
    [JsonPropertyName("t")]
    public string T { get; set; } = string.Empty;

    [JsonPropertyName("o")]
    public decimal? O { get; set; }

    [JsonPropertyName("h")]
    public decimal? H { get; set; }

    [JsonPropertyName("l")]
    public decimal? L { get; set; }

    [JsonPropertyName("c")]
    public decimal? C { get; set; }

    [JsonPropertyName("v")]
    public decimal? V { get; set; }

    [JsonPropertyName("a")]
    public decimal? A { get; set; }

    [JsonPropertyName("pc")]
    public decimal? Pc { get; set; }
}






