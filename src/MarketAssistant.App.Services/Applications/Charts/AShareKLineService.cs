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
public class AShareKLineService : IKLineService
{
    private readonly ZhiTuMarketClient _zhiTuClient;
    private readonly IUserSettingService _userSettingService;
    private readonly ILogger<AShareKLineService> _logger;

    public AShareKLineService(
        ZhiTuMarketClient zhiTuClient,
        ILogger<AShareKLineService> logger,
        IUserSettingService userSettingService)
    {
        _zhiTuClient = zhiTuClient ?? throw new ArgumentNullException(nameof(zhiTuClient));
        _userSettingService = userSettingService;
        _logger = logger;
    }

    /// <summary>
    /// 获取K线数据（统一入口，根据类型调用对应的实现）
    /// </summary>
    public async Task<List<KLineData>> GetKLineDataAsync(string code, KLineType kLineType, int count = 250)
    {
        // 根据K线类型映射到智图API的时间周期参数
        var (interval, dataTypeName) = kLineType switch
        {
            KLineType.Daily => ("d", "日K线"),
            KLineType.Weekly => ("w", "周K线"),
            KLineType.Monthly => ("m", "月K线"),
            KLineType.Minute5 => ("5", "5分钟K线"),
            KLineType.Minute15 => ("15", "15分钟K线"),
            _ => ("d", "日K线") // 默认日K线
        };

        var dataset = await GetKLineDataInternalAsync(code, interval, dataTypeName, count: count);
        return dataset?.Data ?? new List<KLineData>();
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
                Data = new List<KLineData>()
            };

            // 解析数据
            ParseZhiTuKLineData(zhiTuData, klineDataSet);

            // 按调用方请求的 count 截取最近的 N 条（已按时间升序排序，取最后 count 条）
            if (count.HasValue && count.Value > 0 && klineDataSet.Data.Count > count.Value)
            {
                klineDataSet.Data = klineDataSet.Data
                    .Skip(klineDataSet.Data.Count - count.Value)
                    .ToList();
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
            switch (interval.ToLower())
            {
                case "d": // 日K线，默认查询最近1年
                    defaultStartDate = DateTime.Now.AddYears(-1);
                    break;
                case "w": // 周K线，默认查询最近3年
                    defaultStartDate = DateTime.Now.AddYears(-3);
                    break;
                case "m": // 月K线，默认查询最近10年
                    defaultStartDate = DateTime.Now.AddYears(-10);
                    break;
                case "y": // 年K线，默认查询最近10年
                    defaultStartDate = DateTime.Now.AddYears(-10);
                    break;
                case "1":
                case "5":
                case "15":
                case "30":
                case "60": // 分钟级别数据，默认查询最近60天
                    defaultStartDate = DateTime.Now.AddDays(-60);
                    break;
                default: // 其他情况，默认查询最近1年
                    defaultStartDate = DateTime.Now.AddYears(-1);
                    break;
            }

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
                var klineData = new KLineData
                {
                    Timestamp = timestamp,
                    Open = item.O,
                    High = item.H,
                    Low = item.L,
                    Close = item.C,
                    Volume = item.V,
                    Amount = item.A,
                    PreClose = item.Pc
                };

                // 计算涨跌额和涨跌幅
                if (item.Pc > 0)
                {
                    klineData.Change = item.C - item.Pc;
                    klineData.PctChg = (klineData.Change / item.Pc) * 100;
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
        klineDataSet.Data = klineDataSet.Data.OrderBy(x => x.Timestamp).ToList();
    }

    #endregion
}

/// <summary>
/// ZhiTu API K线数据模型
/// </summary>
[Serializable]
internal class ZhiTuKLineData
{
    [JsonPropertyName("t")]
    public string T { get; set; } = string.Empty;

    [JsonPropertyName("o")]
    public decimal O { get; set; }

    [JsonPropertyName("h")]
    public decimal H { get; set; }

    [JsonPropertyName("l")]
    public decimal L { get; set; }

    [JsonPropertyName("c")]
    public decimal C { get; set; }

    [JsonPropertyName("v")]
    public decimal V { get; set; }

    [JsonPropertyName("a")]
    public decimal A { get; set; }

    [JsonPropertyName("pc")]
    public decimal Pc { get; set; }
}






