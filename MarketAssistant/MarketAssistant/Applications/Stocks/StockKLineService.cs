using MarketAssistant.Applications.Stocks.Models;
using MarketAssistant.Infrastructure;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MarketAssistant.Applications.Stocks;

public class StockKLineService
{
    private readonly HttpClient _httpClient;
    private readonly string _tushareApiToken;
    private readonly ILogger<StockKLineService> _logger;
    private const string TUSHARE_API_URL = "http://api.tushare.pro";
    private const string DAILY_FIELDS = "ts_code,trade_date,open,high,low,close,pre_close,change,pct_chg,vol,amount";
    private const string MINUTE_FIELDS = "ts_code,trade_time,open,close,high,low,vol,amount";

    public StockKLineService(ILogger<StockKLineService> logger, IUserSettingService userSettingService)
    {
        _httpClient = new HttpClient();
        _tushareApiToken = userSettingService.CurrentSetting.TushareApiToken;
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
    /// 构建K线请求参数
    /// </summary>
    /// <param name="tsCode">股票代码</param>
    /// <param name="startDate">开始日期</param>
    /// <param name="endDate">结束日期</param>
    /// <returns>参数字典</returns>
    private Dictionary<string, string> BuildKLineParameters(string tsCode, DateTime? startDate, DateTime? endDate)
    {
        var parameters = new Dictionary<string, string>();

        if (!string.IsNullOrEmpty(tsCode))
            parameters.Add("ts_code", tsCode);
        if (startDate.HasValue)
            parameters.Add("start_date", startDate.Value.ToString("yyyyMMdd"));
        if (endDate.HasValue)
            parameters.Add("end_date", endDate.Value.ToString("yyyyMMdd"));

        return parameters;
    }

    /// <summary>
    /// 从Tushare获取数据
    /// </summary>
    /// <param name="apiName">API名称</param>
    /// <param name="parameters">请求参数</param>
    /// <param name="fields">返回字段</param>
    /// <param name="dataType">数据类型描述</param>
    /// <param name="tsCode">股票代码</param>
    /// <returns>API响应数据</returns>
    private async Task<TushareResponse> FetchTushareDataAsync(string apiName, object parameters, string fields, string dataType, string tsCode)
    {
        // 构建日志信息
        string logInfo = !string.IsNullOrEmpty(tsCode) ? $"股票代码: {tsCode}" : "";

        _logger.LogInformation("正在获取股票{DataType}数据: {LogInfo}", dataType, logInfo);

        // 构建请求数据
        var requestData = new
        {
            api_name = apiName,
            token = _tushareApiToken,
            Params = parameters,
            fields
        };

        // 发送请求
        var response = await _httpClient.PostAsJsonAsync(TUSHARE_API_URL, requestData);
        response.EnsureSuccessStatusCode();

        // 解析响应
        var responseData = await response.Content.ReadFromJsonAsync<TushareResponse>();

        if (responseData == null ||
            responseData.Code != 0 ||
            responseData.Data == null ||
            responseData.Data.Items == null ||
            !responseData.Data.Items.Any())
        {
            throw new Exception($"获取{dataType}数据失败: {responseData?.Message ?? "未知错误"}");
        }

        return responseData;
    }

    /// <summary>
    /// 解析日K线数据
    /// </summary>
    /// <param name="responseData">API响应数据</param>
    /// <param name="klineDataSet">K线数据集</param>
    private void ParseDailyKLineData(TushareResponse responseData, StockKLineDataSet klineDataSet)
    {
        if (responseData.Data == null)
        {
            return;
        }
        foreach (var item in responseData.Data.Items)
        {
            if (item.Length >= 11)
            {
                // 解析日期，Tushare返回的日期格式为YYYYMMDD
                if (DateTime.TryParseExact(item[1].ToString(), "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out DateTime timestamp))
                {
                    klineDataSet.Data.Add(new StockKLineData
                    {
                        Timestamp = timestamp,
                        Open = ParseDecimal(item[2]),
                        High = ParseDecimal(item[3]),
                        Low = ParseDecimal(item[4]),
                        Close = ParseDecimal(item[5]),
                        PreClose = ParseDecimal(item[6]),
                        Change = ParseDecimal(item[7]),
                        PctChg = ParseDecimal(item[8]),
                        Volume = ParseDecimal(item[9]),
                        Amount = ParseDecimal(item[10])
                    });
                }
            }
        }

        // 按日期排序（从新到旧）
        klineDataSet.Data = klineDataSet.Data.OrderByDescending(x => x.Timestamp).ToList();
    }

    /// <summary>
    /// 解析分钟K线数据
    /// </summary>
    /// <param name="responseData">API响应数据</param>
    /// <param name="klineDataSet">K线数据集</param>
    private void ParseMinuteKLineData(TushareResponse responseData, StockKLineDataSet klineDataSet)
    {
        foreach (var item in responseData.Data.Items)
        {
            if (item.Length >= 8)
            {
                // 解析日期时间，Tushare返回的格式为yyyy-MM-dd HH:mm:ss
                if (DateTime.TryParse(item[1].ToString(), out DateTime timestamp))
                {
                    klineDataSet.Data.Add(new StockKLineData
                    {
                        Timestamp = timestamp,
                        Open = ParseDecimal(item[2]),
                        Close = ParseDecimal(item[3]),
                        High = ParseDecimal(item[4]),
                        Low = ParseDecimal(item[5]),
                        Volume = ParseDecimal(item[6]),
                        Amount = ParseDecimal(item[7]),
                        // 分钟数据可能没有以下字段，设置为0
                        PreClose = 0,
                        Change = 0,
                        PctChg = 0
                    });
                }
            }
        }

        // 按日期时间排序（从新到旧）
        klineDataSet.Data = klineDataSet.Data.OrderByDescending(x => x.Timestamp).ToList();
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
    /// 从Tushare获取日K线数据
    /// </summary>
    /// <param name="symbol">股票代码（支持多个股票同时提取，逗号分隔）</param>
    /// <param name="startDate">开始日期，默认为null</param>
    /// <param name="endDate">结束日期，默认为null</param>
    /// <param name="tradeDate">交易日期，默认为null</param>
    /// <returns>K线数据集合</returns>
    public async Task<StockKLineDataSet> GetDailyKLineDataAsync(string symbol = "", DateTime? startDate = null, DateTime? endDate = null)
    {
        try
        {
            // 验证参数
            ValidateKLineParameters(symbol);

            // 处理股票代码格式
            string tsCode = string.IsNullOrEmpty(symbol) ? "" : FormatSymbolForTushare(symbol);

            // 构建请求参数
            var parameters = BuildKLineParameters(tsCode, startDate, endDate);

            // 发送请求并获取数据
            var responseData = await FetchTushareDataAsync("daily", parameters, DAILY_FIELDS, "日K线", tsCode);

            // 转换为应用程序数据模型
            var klineDataSet = new StockKLineDataSet
            {
                Symbol = symbol,
                Name = !string.IsNullOrEmpty(tsCode) ? tsCode : "日线数据",
                Interval = "daily",
                Data = new List<StockKLineData>()
            };

            // 解析数据
            ParseDailyKLineData(responseData, klineDataSet);

            return klineDataSet;
        }
        catch (Exception ex)
        {
            LogAndThrowException("日K线", symbol, ex);
            return null; // 不会执行到这里，只是为了满足编译器要求
        }
    }

    /// <summary>
    /// 从Tushare获取周K线数据
    /// </summary>
    /// <param name="symbol">股票代码</param>
    /// <param name="startDate">开始日期，默认为null</param>
    /// <param name="endDate">结束日期，默认为null</param>
    /// <param name="tradeDate">交易日期（每周最后一个交易日期），默认为null</param>
    /// <returns>K线数据集合</returns>
    public async Task<StockKLineDataSet> GetWeeklyKLineDataAsync(string symbol = "", DateTime? startDate = null, DateTime? endDate = null)
    {
        try
        {
            // 验证参数
            ValidateKLineParameters(symbol);

            // 处理股票代码格式
            string tsCode = string.IsNullOrEmpty(symbol) ? "" : FormatSymbolForTushare(symbol);

            // 构建请求参数
            var parameters = BuildKLineParameters(tsCode, startDate, endDate);

            // 发送请求并获取数据
            var responseData = await FetchTushareDataAsync("weekly", parameters, DAILY_FIELDS, "周K线", tsCode);

            // 转换为应用程序数据模型
            var klineDataSet = new StockKLineDataSet
            {
                Symbol = symbol,
                Name = !string.IsNullOrEmpty(tsCode) ? tsCode : "周线数据",
                Interval = "weekly",
                Data = new List<StockKLineData>()
            };

            // 解析数据
            ParseDailyKLineData(responseData, klineDataSet);

            return klineDataSet;
        }
        catch (Exception ex)
        {
            LogAndThrowException("周K线", symbol, ex);
            return null; // 不会执行到这里，只是为了满足编译器要求
        }
    }

    /// <summary>
    /// 从Tushare获取月K线数据
    /// </summary>
    /// <param name="symbol">股票代码</param>
    /// <param name="startDate">开始日期，默认为null</param>
    /// <param name="endDate">结束日期，默认为null</param>
    /// <param name="tradeDate">交易日期（每月最后一个交易日日期），默认为null</param>
    /// <returns>K线数据集合</returns>
    public async Task<StockKLineDataSet> GetMonthlyKLineDataAsync(string symbol = "", DateTime? startDate = null, DateTime? endDate = null)
    {
        try
        {
            // 验证参数
            ValidateKLineParameters(symbol);

            // 处理股票代码格式
            string tsCode = string.IsNullOrEmpty(symbol) ? "" : FormatSymbolForTushare(symbol);

            // 构建请求参数
            var parameters = BuildKLineParameters(tsCode, startDate, endDate);

            // 发送请求并获取数据
            var responseData = await FetchTushareDataAsync("monthly", parameters, DAILY_FIELDS, "月K线", tsCode);

            // 转换为应用程序数据模型
            var klineDataSet = new StockKLineDataSet
            {
                Symbol = symbol,
                Name = !string.IsNullOrEmpty(tsCode) ? tsCode : "月线数据",
                Interval = "monthly",
                Data = new List<StockKLineData>()
            };

            // 解析数据
            ParseDailyKLineData(responseData, klineDataSet);

            return klineDataSet;
        }
        catch (Exception ex)
        {
            LogAndThrowException("月K线", symbol, ex);
            return null;
        }
    }

    /// <summary>
    /// 将股票代码格式化为Tushare API所需格式
    /// </summary>
    /// <param name="symbol">原始股票代码</param>
    /// <returns>格式化后的股票代码</returns>
    private string FormatSymbolForTushare(string symbol)
    {
        // 如果已经是Tushare格式，直接返回
        if (symbol.Contains("."))
        {
            return symbol;
        }

        // 处理常见的A股代码格式
        if (symbol.StartsWith("sz") || symbol.StartsWith("sh"))
        {
            string code = symbol.Substring(2);
            string market = symbol.StartsWith("sz") ? "SZ" : "SH";
            return $"{code}.{market}";
        }

        // 如果是纯数字代码，根据规则判断市场
        if (symbol.All(char.IsDigit))
        {
            // 深市：000、001、002、003、300、301、399开头
            if (symbol.StartsWith("000") || symbol.StartsWith("001") ||
                symbol.StartsWith("002") || symbol.StartsWith("003") ||
                symbol.StartsWith("300") || symbol.StartsWith("301") ||
                symbol.StartsWith("399"))
            {
                return $"{symbol}.SZ";
            }
            // 沪市：600、601、603、605、688、900开头
            else if (symbol.StartsWith("600") || symbol.StartsWith("601") ||
                     symbol.StartsWith("603") || symbol.StartsWith("605") ||
                     symbol.StartsWith("688") || symbol.StartsWith("900"))
            {
                return $"{symbol}.SH";
            }
        }

        // 默认返回原始代码
        return symbol;
    }

    /// <summary>
    /// 安全解析decimal值
    /// </summary>
    /// <param name="value">要解析的值</param>
    /// <returns>解析后的decimal值，解析失败返回0</returns>
    private decimal ParseDecimal(object value)
    {
        if (value == null)
        {
            return 0;
        }

        if (decimal.TryParse(value.ToString(), out decimal result))
        {
            return result;
        }

        return 0;
    }

    /// <summary>
    /// 从Tushare获取分钟级K线数据
    /// </summary>
    /// <param name="symbol">股票代码</param>
    /// <param name="freq">分钟频度（1min/5min/15min/30min/60min）</param>
    /// <param name="startDateTime">开始日期时间，格式：2023-08-25 09:00:00</param>
    /// <param name="endDateTime">结束日期时间，格式：2023-08-25 19:00:00</param>
    /// <returns>K线数据集合</returns>
    public async Task<StockKLineDataSet> GetMinuteKLineDataAsync(string symbol, string freq, string startDateTime = null, string endDateTime = null)
    {
        try
        {
            // 验证频率参数
            if (!IsValidFreq(freq))
            {
                throw new ArgumentException("无效的分钟频度参数，有效值为：1min, 5min, 15min, 30min, 60min", nameof(freq));
            }

            if (string.IsNullOrEmpty(symbol))
            {
                throw new ArgumentException("股票代码参数必须提供", nameof(symbol));
            }

            // 处理股票代码格式
            string tsCode = FormatSymbolForTushare(symbol);

            // 构建请求参数
            var parameters = new Dictionary<string, object>
            {
                { "ts_code", tsCode },
                { "freq", freq }
            };

            if (!string.IsNullOrEmpty(startDateTime))
                parameters.Add("start_date", startDateTime);
            if (!string.IsNullOrEmpty(endDateTime))
                parameters.Add("end_date", endDateTime);

            // 发送请求并获取数据
            var responseData = await FetchTushareDataAsync("stk_mins", parameters, MINUTE_FIELDS, $"{freq}分钟K线", tsCode);

            // 转换为应用程序数据模型
            var klineDataSet = new StockKLineDataSet
            {
                Symbol = symbol,
                Name = tsCode,
                Interval = freq,
                Data = new List<StockKLineData>()
            };

            // 解析数据
            ParseMinuteKLineData(responseData, klineDataSet);

            return klineDataSet;
        }
        catch (Exception ex)
        {
            LogAndThrowException($"{freq}分钟K线", symbol, ex);
            return null; // 不会执行到这里，只是为了满足编译器要求
        }
    }

    /// <summary>
    /// 验证分钟频度参数是否有效
    /// </summary>
    /// <param name="freq">分钟频度参数</param>
    /// <returns>是否有效</returns>
    private bool IsValidFreq(string freq)
    {
        string[] validFreqs = { "1min", "5min", "15min", "30min", "60min" };
        return validFreqs.Contains(freq);
    }
}

/// <summary>
/// Tushare API响应模型
/// </summary>
[Serializable]
public class TushareResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("msg")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public TushareData? Data { get; set; }
}

/// <summary>
/// Tushare数据模型
/// </summary>
[Serializable]
public class TushareData
{
    [JsonPropertyName("fields")]
    public List<string> Fields { get; set; } = new List<string>();

    [JsonPropertyName("items")]
    public List<object[]> Items { get; set; } = new List<object[]>();
}