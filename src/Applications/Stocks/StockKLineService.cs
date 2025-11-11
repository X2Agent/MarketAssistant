using MarketAssistant.Applications.Stocks.Models;
using MarketAssistant.Infrastructure;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarketAssistant.Applications.Stocks;

public class StockKLineService
{
    private readonly HttpClient _httpClient;
    private readonly string _zhiTuApiToken;
    private readonly ILogger<StockKLineService> _logger;
    private const string ZHITU_API_BASE_URL = "https://api.zhituapi.com/hs/history";

    public StockKLineService(ILogger<StockKLineService> logger, IUserSettingService userSettingService)
    {
        _httpClient = new HttpClient();
        _zhiTuApiToken = userSettingService.CurrentSetting.ZhiTuApiToken;
        _logger = logger;
    }

    #region 通用辅助方法

    /// <summary>
    /// 验证K线参数
    /// </summary>
    /// <param name="symbol">股票代码</param>
    private void ValidateKLineParameters(string symbol)
    {
        if (string.IsNullOrEmpty(symbol))
        {
            throw new ArgumentException("股票代码参数必须提供");
        }
    }

    /// <summary>
    /// 构建zhituapi请求URL
    /// </summary>
    /// <param name="symbol">股票代码（如000001.SZ）</param>
    /// <param name="interval">分时级别（如d、w、m、y、1、5、15、30、60）</param>
    /// <param name="adjustType">除权方式（n、f、b、fr、br）</param>
    /// <param name="startDate">开始时间</param>
    /// <param name="endDate">结束时间</param>
    /// <returns>完整的API URL</returns>
    private string BuildZhiTuApiUrl(string symbol, string interval, string adjustType = "n", DateTime? startDate = null, DateTime? endDate = null)
    {
        var url = $"{ZHITU_API_BASE_URL}/{symbol}/{interval}/{adjustType}?token={_zhiTuApiToken}";

        // 如果没有指定时间范围，根据不同的interval设置合理的默认时间范围
        DateTime defaultStartDate;
        DateTime defaultEndDate = DateTime.Now;

        if (!startDate.HasValue && !endDate.HasValue)
        {
            switch (interval.ToLower())
            {
                case "d": // 日K线，默认查询最近1年
                    defaultStartDate = DateTime.Now.AddMonths(-6);
                    break;
                case "w": // 周K线，默认查询最近2年
                    defaultStartDate = DateTime.Now.AddYears(-1);
                    break;
                case "m": // 月K线，默认查询最近5年
                    defaultStartDate = DateTime.Now.AddYears(-3);
                    break;
                case "y": // 年K线，默认查询最近10年
                    defaultStartDate = DateTime.Now.AddYears(-10);
                    break;
                case "1":
                case "5":
                case "15":
                case "30":
                case "60": // 分钟级别数据，默认查询最近30天
                    defaultStartDate = DateTime.Now.AddDays(-30);
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

    /// <summary>
    /// 从zhituapi获取数据
    /// </summary>
    /// <param name="url">API URL</param>
    /// <param name="dataType">数据类型描述</param>
    /// <param name="symbol">股票代码</param>
    /// <returns>API响应数据</returns>
    private async Task<List<ZhiTuKLineData>> FetchZhiTuDataAsync(string url, string dataType, string symbol)
    {
        _logger.LogInformation("正在获取股票{DataType}数据: 股票代码: {Symbol}", dataType, symbol);

        try
        {
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync();
            var zhiTuData = JsonSerializer.Deserialize<List<ZhiTuKLineData>>(jsonContent);

            if (zhiTuData == null || !zhiTuData.Any())
            {
                throw new Exception($"获取{dataType}数据失败: 返回数据为空");
            }

            return zhiTuData;
        }
        catch (HttpRequestException ex)
        {
            throw new Exception($"获取{dataType}数据失败: 网络请求错误 - {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new Exception($"获取{dataType}数据失败: 数据解析错误 - {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 解析zhituapi K线数据
    /// </summary>
    /// <param name="zhiTuData">zhituapi响应数据</param>
    /// <param name="klineDataSet">K线数据集</param>
    private void ParseZhiTuKLineData(List<ZhiTuKLineData> zhiTuData, StockKLineDataSet klineDataSet)
    {
        foreach (var item in zhiTuData)
        {
            // 解析时间戳
            if (DateTime.TryParse(item.T, out DateTime timestamp))
            {
                var klineData = new StockKLineData
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

    /// <summary>
    /// 记录错误并抛出异常
    /// </summary>
    /// <param name="dataType">数据类型</param>
    /// <param name="symbol">股票代码</param>
    /// <param name="ex">异常</param>
    private void LogAndThrowException(string dataType, string symbol, Exception ex)
    {
        // 构建错误日志信息
        string errorInfo = !string.IsNullOrEmpty(symbol) ? $"股票代码: {symbol}" : "";

        _logger.LogError(ex, "获取{DataType}数据时发生错误 - {ErrorInfo}", dataType, errorInfo);
        throw new Exception($"获取{dataType}数据失败: {ex.Message}", ex);
    }

    #endregion

    /// <summary>
    /// 从zhituapi获取日K线数据
    /// </summary>
    /// <param name="symbol">股票代码（如000001.SZ）</param>
    /// <param name="startDate">开始日期，默认为null</param>
    /// <param name="endDate">结束日期，默认为null</param>
    /// <param name="adjustType">除权方式（n=不复权，f=前复权，b=后复权，fr=等比前复权，br=等比后复权），默认为n</param>
    /// <returns>K线数据集合</returns>
    public async Task<StockKLineDataSet> GetDailyKLineDataAsync(string symbol, DateTime? startDate = null, DateTime? endDate = null, string adjustType = "n")
    {
        try
        {
            // 验证参数
            ValidateKLineParameters(symbol);

            // 处理股票代码格式
            string formattedSymbol = StockSymbolConverter.ToZhiTuFormat(symbol);

            // 构建API URL
            string apiUrl = BuildZhiTuApiUrl(formattedSymbol, "d", adjustType, startDate, endDate);

            // 发送请求并获取数据
            var zhiTuData = await FetchZhiTuDataAsync(apiUrl, "日K线", symbol);

            // 转换为应用程序数据模型
            var klineDataSet = new StockKLineDataSet
            {
                Symbol = symbol,
                Interval = "daily",
                Data = new List<StockKLineData>()
            };

            // 解析数据
            ParseZhiTuKLineData(zhiTuData, klineDataSet);

            return klineDataSet;
        }
        catch (Exception ex)
        {
            LogAndThrowException("日K线", symbol, ex);
            throw; // LogAndThrowException 已经抛出异常，这里不会执行到
        }
    }

    /// <summary>
    /// 从zhituapi获取周K线数据
    /// </summary>
    /// <param name="symbol">股票代码（如000001.SZ）</param>
    /// <param name="startDate">开始日期，默认为null</param>
    /// <param name="endDate">结束日期，默认为null</param>
    /// <param name="adjustType">除权方式（n=不复权，f=前复权，b=后复权，fr=等比前复权，br=等比后复权），默认为n</param>
    /// <returns>K线数据集合</returns>
    public async Task<StockKLineDataSet> GetWeeklyKLineDataAsync(string symbol, DateTime? startDate = null, DateTime? endDate = null, string adjustType = "n")
    {
        try
        {
            // 验证参数
            ValidateKLineParameters(symbol);

            // 处理股票代码格式
            string formattedSymbol = StockSymbolConverter.ToZhiTuFormat(symbol);

            // 构建API URL
            string apiUrl = BuildZhiTuApiUrl(formattedSymbol, "w", adjustType, startDate, endDate);

            // 发送请求并获取数据
            var zhiTuData = await FetchZhiTuDataAsync(apiUrl, "周K线", symbol);

            // 转换为应用程序数据模型
            var klineDataSet = new StockKLineDataSet
            {
                Symbol = symbol,
                Interval = "weekly",
                Data = new List<StockKLineData>()
            };

            // 解析数据
            ParseZhiTuKLineData(zhiTuData, klineDataSet);

            return klineDataSet;
        }
        catch (Exception ex)
        {
            LogAndThrowException("周K线", symbol, ex);
            throw; // LogAndThrowException 已经抛出异常，这里不会执行到
        }
    }

    /// <summary>
    /// 从zhituapi获取月K线数据
    /// </summary>
    /// <param name="symbol">股票代码（如000001.SZ）</param>
    /// <param name="startDate">开始日期，默认为null</param>
    /// <param name="endDate">结束日期，默认为null</param>
    /// <param name="adjustType">除权方式（n=不复权，f=前复权，b=后复权，fr=等比前复权，br=等比后复权），默认为n</param>
    /// <returns>K线数据集合</returns>
    public async Task<StockKLineDataSet> GetMonthlyKLineDataAsync(string symbol, DateTime? startDate = null, DateTime? endDate = null, string adjustType = "n")
    {
        try
        {
            // 验证参数
            ValidateKLineParameters(symbol);

            // 处理股票代码格式
            string formattedSymbol = StockSymbolConverter.ToZhiTuFormat(symbol);

            // 构建API URL
            string apiUrl = BuildZhiTuApiUrl(formattedSymbol, "m", adjustType, startDate, endDate);

            // 发送请求并获取数据
            var zhiTuData = await FetchZhiTuDataAsync(apiUrl, "月K线", symbol);

            // 转换为应用程序数据模型
            var klineDataSet = new StockKLineDataSet
            {
                Symbol = symbol,
                Interval = "monthly",
                Data = new List<StockKLineData>()
            };

            // 解析数据
            ParseZhiTuKLineData(zhiTuData, klineDataSet);

            return klineDataSet;
        }
        catch (Exception ex)
        {
            LogAndThrowException("月K线", symbol, ex);
            throw; // LogAndThrowException 已经抛出异常，这里不会执行到
        }
    }

    /// <summary>
    /// 从zhituapi获取分钟级K线数据
    /// </summary>
    /// <param name="symbol">股票代码（如000001.SZ）</param>
    /// <param name="interval">分钟频度（1、5、15、30、60）</param>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    /// <param name="adjustType">除权方式（n=不复权，f=前复权，b=后复权，fr=等比前复权，br=等比后复权），默认为n</param>
    /// <returns>K线数据集合</returns>
    public async Task<StockKLineDataSet> GetMinuteKLineDataAsync(string symbol, string interval, DateTime? startDate = null, DateTime? endDate = null, string adjustType = "n")
    {
        try
        {
            // 验证频率参数
            if (!IsValidInterval(interval))
            {
                throw new ArgumentException("无效的分钟频度参数，有效值为：1, 5, 15, 30, 60", nameof(interval));
            }

            // 验证参数
            ValidateKLineParameters(symbol);

            // 处理股票代码格式
            string formattedSymbol = StockSymbolConverter.ToZhiTuFormat(symbol);

            // 构建API URL
            string apiUrl = BuildZhiTuApiUrl(formattedSymbol, interval, adjustType, startDate, endDate);

            // 发送请求并获取数据
            var zhiTuData = await FetchZhiTuDataAsync(apiUrl, $"{interval}分钟K线", symbol);

            // 转换为应用程序数据模型
            var klineDataSet = new StockKLineDataSet
            {
                Symbol = symbol,
                Interval = $"{interval}min",
                Data = new List<StockKLineData>()
            };

            // 解析数据
            ParseZhiTuKLineData(zhiTuData, klineDataSet);

            return klineDataSet;
        }
        catch (Exception ex)
        {
            LogAndThrowException($"{interval}分钟K线", symbol, ex);
            throw; // LogAndThrowException 已经抛出异常，这里不会执行到
        }
    }

    /// <summary>
    /// 验证分钟频度参数是否有效
    /// </summary>
    /// <param name="interval">分钟频度参数</param>
    /// <returns>是否有效</returns>
    private bool IsValidInterval(string interval)
    {
        string[] validIntervals = { "1", "5", "15", "30", "60" };
        return validIntervals.Contains(interval);
    }

    /// <summary>
    /// 从zhituapi获取年K线数据
    /// </summary>
    /// <param name="symbol">股票代码（如000001.SZ）</param>
    /// <param name="startDate">开始日期，默认为null</param>
    /// <param name="endDate">结束日期，默认为null</param>
    /// <param name="adjustType">除权方式（n=不复权，f=前复权，b=后复权，fr=等比前复权，br=等比后复权），默认为n</param>
    /// <returns>K线数据集合</returns>
    public async Task<StockKLineDataSet> GetYearlyKLineDataAsync(string symbol, DateTime? startDate = null, DateTime? endDate = null, string adjustType = "n")
    {
        try
        {
            // 验证参数
            ValidateKLineParameters(symbol);

            // 处理股票代码格式
            string formattedSymbol = StockSymbolConverter.ToZhiTuFormat(symbol);

            // 构建API URL
            string apiUrl = BuildZhiTuApiUrl(formattedSymbol, "y", adjustType, startDate, endDate);

            // 发送请求并获取数据
            var zhiTuData = await FetchZhiTuDataAsync(apiUrl, "年K线", symbol);

            // 转换为应用程序数据模型
            var klineDataSet = new StockKLineDataSet
            {
                Symbol = symbol,
                Interval = "yearly",
                Data = new List<StockKLineData>()
            };

            // 解析数据
            ParseZhiTuKLineData(zhiTuData, klineDataSet);

            return klineDataSet;
        }
        catch (Exception ex)
        {
            LogAndThrowException("年K线", symbol, ex);
            throw; // LogAndThrowException 已经抛出异常，这里不会执行到
        }
    }
}

/// <summary>
/// ZhiTu API K线数据模型
/// </summary>
[Serializable]
public class ZhiTuKLineData
{
    /// <summary>
    /// 交易时间
    /// </summary>
    [JsonPropertyName("t")]
    public string T { get; set; } = string.Empty;

    /// <summary>
    /// 开盘价
    /// </summary>
    [JsonPropertyName("o")]
    public decimal O { get; set; }

    /// <summary>
    /// 最高价
    /// </summary>
    [JsonPropertyName("h")]
    public decimal H { get; set; }

    /// <summary>
    /// 最低价
    /// </summary>
    [JsonPropertyName("l")]
    public decimal L { get; set; }

    /// <summary>
    /// 收盘价
    /// </summary>
    [JsonPropertyName("c")]
    public decimal C { get; set; }

    /// <summary>
    /// 成交量
    /// </summary>
    [JsonPropertyName("v")]
    public decimal V { get; set; }

    /// <summary>
    /// 成交额
    /// </summary>
    [JsonPropertyName("a")]
    public decimal A { get; set; }

    /// <summary>
    /// 前收盘价
    /// </summary>
    [JsonPropertyName("pc")]
    public decimal Pc { get; set; }

    /// <summary>
    /// 停牌标志（1停牌，0不停牌）
    /// </summary>
    ///[JsonPropertyName("sf")]
    ///public int Sf { get; set; }
}