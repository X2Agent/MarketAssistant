using MarketAssistant.Applications.Charts.Models;
using MarketAssistant.Infrastructure;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using static MarketAssistant.Infrastructure.Core.CryptoSymbolConverter;
namespace MarketAssistant.Applications.Charts;

/// <summary>
/// 虚拟币K线数据服务实现（基于币安API）
/// 文档：https://developers.binance.com/docs/zh-CN/binance-spot-api-docs/rest-api
/// </summary>
public class CryptoKLineService : IKLineService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CryptoKLineService> _logger;

    // 币安公开市场数据API（无需API Key）
    private const string BINANCE_API_BASE_URL = "https://api.binance.com";
    private const int DEFAULT_LIMIT = 500; // 币安API最多返回1000条，默认500条

    public CryptoKLineService(ILogger<CryptoKLineService> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>
    /// 获取K线数据（统一入口，根据类型调用对应的实现）
    /// </summary>
    public async Task<List<KLineData>> GetKLineDataAsync(string code, KLineType kLineType, int count = 100)
    {
        // 根据K线类型映射到币安API的时间周期参数
        var interval = kLineType switch
        {
            KLineType.Daily => "1d",
            KLineType.Weekly => "1w",
            KLineType.Monthly => "1M",
            KLineType.Minute5 => "5m",
            KLineType.Minute15 => "15m",
            _ => "1d" // 默认日K线
        };

        return await GetKLineDataInternalAsync(code, interval, count);
    }

    #region 内部实现

    /// <summary>
    /// 获取K线数据（内部实现）
    /// </summary>
    /// <param name="symbol">交易对，如 BTCUSDT</param>
    /// <param name="interval">时间间隔：1m, 5m, 15m, 30m, 1h, 1d, 1w, 1M</param>
    /// <param name="limit">返回数据条数，最大1000</param>
    private async Task<List<KLineData>> GetKLineDataInternalAsync(string symbol, string interval, int limit = 100)
    {
        try
        {
            // 验证参数
            if (string.IsNullOrEmpty(symbol))
            {
                throw new ArgumentException("交易对代码不能为空");
            }

            // 格式化交易对代码（转换为币安格式，如 BTCUSDT）
            string formattedSymbol = ToBinanceFormat(symbol);

            // 限制请求数量（币安限制最大1000）
            int requestLimit = Math.Min(limit, 1000);

            // 构建API URL
            string apiUrl = $"{BINANCE_API_BASE_URL}/api/v3/klines?symbol={formattedSymbol}&interval={interval}&limit={requestLimit}";

            _logger.LogInformation("正在获取币安K线数据: {Symbol}, 周期: {Interval}, 数量: {Limit}", formattedSymbol, interval, requestLimit);

            // 发送HTTP请求
            var response = await _httpClient.GetAsync(apiUrl);
            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync();

            // 币安返回的是数组的数组格式
            var binanceData = JsonSerializer.Deserialize<List<List<JsonElement>>>(jsonContent);

            if (binanceData == null || !binanceData.Any())
            {
                _logger.LogWarning("币安API返回数据为空: {Symbol}", formattedSymbol);
                return new List<KLineData>();
            }

            // 转换为应用程序数据模型
            var klineDataList = new List<KLineData>();

            foreach (var item in binanceData)
            {
                if (item.Count < 11)
                {
                    _logger.LogWarning("币安K线数据格式异常，跳过该条数据");
                    continue;
                }

                // 币安K线数据格式（数组）：
                // [0] 开盘时间（毫秒）
                // [1] 开盘价
                // [2] 最高价
                // [3] 最低价
                // [4] 收盘价
                // [5] 成交量
                // [6] 收盘时间（毫秒）
                // [7] 成交额
                // [8] 成交笔数
                // [9] 主动买入成交量
                // [10] 主动买入成交额
                // [11] 忽略

                var klineData = new KLineData
                {
                    Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(item[0].GetInt64()).DateTime,
                    Open = decimal.Parse(item[1].GetString() ?? "0"),
                    High = decimal.Parse(item[2].GetString() ?? "0"),
                    Low = decimal.Parse(item[3].GetString() ?? "0"),
                    Close = decimal.Parse(item[4].GetString() ?? "0"),
                    Volume = decimal.Parse(item[5].GetString() ?? "0"),
                    Amount = decimal.Parse(item[7].GetString() ?? "0")
                };

                klineDataList.Add(klineData);
            }

            // 计算涨跌额和涨跌幅
            CalculatePriceChanges(klineDataList);

            _logger.LogInformation("成功获取币安K线数据: {Symbol}, 返回 {Count} 条记录", formattedSymbol, klineDataList.Count);

            return klineDataList;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "获取币安K线数据失败 - 网络请求错误: {Symbol}", symbol);
            throw new FriendlyException($"获取虚拟币K线数据失败: 网络连接错误 - {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "获取币安K线数据失败 - JSON解析错误: {Symbol}", symbol);
            throw new FriendlyException($"获取虚拟币K线数据失败: 数据解析错误 - {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取币安K线数据失败: {Symbol}", symbol);
            throw new FriendlyException($"获取虚拟币K线数据失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 计算涨跌额和涨跌幅
    /// </summary>
    private void CalculatePriceChanges(List<KLineData> klineDataList)
    {
        for (int i = 0; i < klineDataList.Count; i++)
        {
            if (i == 0)
            {
                // 第一条数据，无法计算涨跌
                klineDataList[i].PreClose = klineDataList[i].Open;
                klineDataList[i].Change = 0;
                klineDataList[i].PctChg = 0;
            }
            else
            {
                // 使用前一条数据的收盘价作为昨收价
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

    #endregion
}






