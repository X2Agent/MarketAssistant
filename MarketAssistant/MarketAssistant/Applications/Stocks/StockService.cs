using MarketAssistant.Applications.Stocks.Models;
using MarketAssistant.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using System.Text.Json;

namespace MarketAssistant.Applications.Stocks;

public class StockService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<StockService> _logger;
    private readonly PlaywrightService _playwrightService;

    public StockService(ILogger<StockService> logger, PlaywrightService playwrightService)
    {
        _httpClient = new HttpClient();
        _logger = logger;
        _playwrightService = playwrightService;
    }

    public async Task<List<(string Name, string Code)>> SearchStockAsync(string keyword, CancellationToken cancellationToken = default)
    {
        var url = $"https://www.cls.cn/searchPage?keyword={keyword.Trim()}&type=stock";

        return await _playwrightService.ExecuteWithPageAsync(async page =>
        {
            var stockList = new List<(string Name, string Code)>();

            await page.GotoAsync(url);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            var stockElements = await page.QuerySelectorAllAsync(".search-stock-list");

            foreach (var stockElement in stockElements)
            {
                var nameElement = await stockElement.QuerySelectorAsync("a.search-content");
                var codeElement = await stockElement.QuerySelectorAsync("a.search-content + a.search-content");

                if (nameElement != null && codeElement != null)
                {
                    var name = await nameElement.InnerTextAsync();
                    var code = (await codeElement.InnerHTMLAsync()).Replace("<em>", "").Replace("</em>", "").Trim();
                    stockList.Add((name, code));
                }
            }

            return stockList;
        }, cancellationToken: cancellationToken);
    }

    public async Task<StockInfo> GetStockInfoAsync(string stockCode, string market = "", CancellationToken cancellationToken = default)
    {
        // 创建股票信息对象
        var stockInfo = new StockInfo
        {
            Code = stockCode,
            Name = "未知股票",
            Market = market
        };

        // 尝试获取股票数据
        try
        {
            // 构建股票详情页URL
            var fullCode = string.IsNullOrEmpty(market) ? stockCode : $"{market}{stockCode}".ToLower();
            var url = $"https://www.cls.cn/stock?code={fullCode}";

            // 使用Playwright获取股票详情页
            await _playwrightService.ExecuteWithPageAsync(async page =>
            {
                await page.GotoAsync(url);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

                // 获取股票名称和代码 - 根据提供的HTML结构
                var stockDetailElement = await page.QuerySelectorAsync(".stock-detail");
                if (stockDetailElement != null)
                {
                    // 获取股票名称
                    var nameElement = await stockDetailElement.QuerySelectorAsync(".f-s-25.f-w-b");
                    if (nameElement != null)
                    {
                        stockInfo.Name = (await nameElement.InnerTextAsync()).Trim();
                    }

                    // 获取股票代码和市场
                    var codeElement = await stockDetailElement.QuerySelectorAsync(".f-s-20.f-w-b");
                    if (codeElement != null)
                    {
                        var fullCodeText = (await codeElement.InnerTextAsync()).Trim();
                        if (!string.IsNullOrEmpty(fullCodeText))
                        {
                            // 从代码中提取市场信息（如sh601138中的sh表示上海市场）
                            if (fullCodeText.StartsWith("sh", StringComparison.OrdinalIgnoreCase))
                            {
                                stockInfo.Market = "SH";
                                stockInfo.Code = fullCodeText.Substring(2);
                            }
                            else if (fullCodeText.StartsWith("sz", StringComparison.OrdinalIgnoreCase))
                            {
                                stockInfo.Market = "SZ";
                                stockInfo.Code = fullCodeText.Substring(2);
                            }
                            else
                            {
                                stockInfo.Code = fullCodeText;
                            }
                        }
                    }
                }

                // 获取价格和涨跌幅 - 根据提供的HTML结构
                var quoteChangeBox = await page.QuerySelectorAsync(".quote-change-box");
                if (quoteChangeBox != null)
                {
                    // 获取当前价格
                    var priceElement = await quoteChangeBox.QuerySelectorAsync(".quote-price");
                    if (priceElement != null)
                    {
                        stockInfo.CurrentPrice = (await priceElement.InnerTextAsync()).Trim();
                    }

                    // 获取涨跌幅
                    var changeElement = await quoteChangeBox.QuerySelectorAsync(".quote-change");
                    if (changeElement != null)
                    {
                        var changeText = (await changeElement.InnerTextAsync()).Trim();
                        // 提取涨跌幅百分比
                        if (changeText.Contains("%"))
                        {
                            var startIndex = changeText.IndexOf("(") + 1;
                            var endIndex = changeText.IndexOf("%") + 1;
                            if (startIndex > 0 && endIndex > startIndex)
                            {
                                stockInfo.ChangePercentage = changeText.Substring(startIndex, endIndex - startIndex);
                            }
                            else
                            {
                                stockInfo.ChangePercentage = changeText;
                            }
                        }
                        else
                        {
                            stockInfo.ChangePercentage = changeText;
                        }
                    }
                }

                // 获取所属板块 - 根据提供的HTML结构
                var stockRelatedBox = await page.QuerySelectorAsync(".stock-related-box");
                if (stockRelatedBox != null)
                {
                    var stockPlage = await stockRelatedBox.QuerySelectorAsync(".stock-related-plate");

                    if (stockPlage != null)
                    {
                        var sectorElement = await stockPlage.QuerySelectorAsync(".m-r-10.f-s-20.c-222.f-w-b");
                        if (sectorElement != null)
                        {
                            stockInfo.SectorName = (await sectorElement.InnerTextAsync()).Trim();
                        }
                    }
                    // 获取所有板块元素
                    //var stockRelatedPlates = await stockRelatedBox.QuerySelectorAllAsync(".stock-related-plate");
                }

                return stockInfo;
            }, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"获取股票详细数据异常: {ex.Message}");
        }

        return stockInfo;
    }

    public async Task<List<HotStock>> GetHotStocksAsync()
    {
        try
        {
            Console.WriteLine("GetHotStocksAsync: 获取今日A股热搜股票数据");

            // 获取当前日期
            DateTime today = DateTime.Now;

            // 如果是周六或周日，调整为最近的周五
            if (today.DayOfWeek == DayOfWeek.Saturday)
            {
                today = today.AddDays(-1); // 周六减一天为周五
            }
            else if (today.DayOfWeek == DayOfWeek.Sunday)
            {
                today = today.AddDays(-2); // 周日减两天为周五
            }

            // 格式化日期为yyyyMMdd
            string formattedDate = today.ToString("yyyyMMdd");

            // 百度股市通API地址，将固定日期替换为当前日期
            var url = $"https://finance.pae.baidu.com/vapi/v1/hotrank?product=stock&day={formattedDate}&pn=0&rn=8&market=ab&type=day&finClientType=pc";

            var response = await _httpClient.GetStringAsync(url);
            var jsonDocument = JsonDocument.Parse(response);
            var root = jsonDocument.RootElement;

            // 检查API返回状态
            if (!root.TryGetProperty("Result", out var resultElement))
            {
                Console.WriteLine("GetHotStocksAsync: API返回数据格式不正确，缺少Result字段");
                return new List<HotStock>();
            }

            // 检查header和body数组
            if (!resultElement.TryGetProperty("header", out var headerElement) ||
                !resultElement.TryGetProperty("body", out var bodyElement))
            {
                Console.WriteLine("GetHotStocksAsync: API返回数据格式不正确，缺少header或body字段");
                return new List<HotStock>();
            }

            // 解析header数组，获取字段索引
            var headerIndices = new Dictionary<string, int>();
            int index = 0;
            foreach (var header in headerElement.EnumerateArray())
            {
                headerIndices[header.GetString() ?? string.Empty] = index++;
            }

            var hotStocks = new List<HotStock>();

            // 遍历body数组中的每个股票数据
            foreach (var stockArray in bodyElement.EnumerateArray())
            {
                // 确保数组长度与header长度一致
                if (stockArray.GetArrayLength() != headerIndices.Count)
                {
                    Console.WriteLine("GetHotStocksAsync: 股票数据数组长度与header不匹配");
                    continue;
                }

                var stockData = stockArray.EnumerateArray().ToArray();

                var hotStock = new HotStock
                {
                    Name = stockData[headerIndices["股票名称"]].GetString() ?? string.Empty,
                    ChangePercentage = stockData[headerIndices["涨跌幅"]].GetString() ?? string.Empty,
                    SectorName = stockData[headerIndices["所属板块名称"]].GetString() ?? string.Empty,
                    Code = stockData[headerIndices["市场代码"]].GetString() ?? string.Empty,
                    CurrentPrice = stockData[headerIndices["现价"]].GetString() ?? string.Empty,
                    Market = stockData[headerIndices["市场缩写"]].GetString() ?? string.Empty,
                    RankChange = stockData[headerIndices["排名变化"]].GetString() ?? string.Empty,
                    MarketType = stockData[headerIndices["市场"]].GetString() ?? string.Empty,
                    HeatIndex = stockData[headerIndices["综合热度"]].GetString() ?? string.Empty
                };

                hotStocks.Add(hotStock);
            }

            return hotStocks;
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"GetHotStocksAsync HTTP请求异常: {ex.Message}");
            throw;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"GetHotStocksAsync JSON解析异常: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GetHotStocksAsync未知异常: {ex.Message}");
            throw;
        }
    }
}
