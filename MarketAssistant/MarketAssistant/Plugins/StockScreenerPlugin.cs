using MarketAssistant.Infrastructure;
using MarketAssistant.Plugins.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace MarketAssistant.Plugins;

/// <summary>
/// 投资网站股票筛选插件，通过Playwright自动化操作investing.com股票筛选器
/// TODO https://xueqiu.com/stock/screener
/// </summary>
public sealed class StockScreenerPlugin
{
    private readonly PlaywrightService _playwrightService;
    private readonly ILogger<StockScreenerPlugin> _logger;
    private const string INVESTING_SCREENER_URL = "https://cn.investing.com/stock-screener";

    public StockScreenerPlugin(
        PlaywrightService playwrightService,
        ILogger<StockScreenerPlugin> logger)
    {
        _playwrightService = playwrightService ?? throw new ArgumentNullException(nameof(playwrightService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 根据筛选条件获取股票列表
    /// </summary>
    [KernelFunction("screen_stocks_by_criteria"), Description("根据用户指定的筛选条件从investing.com获取股票列表")]
    public async Task<List<InvestingStockInfo>> ScreenStocksByCriteriaAsync(
        [Description("国家/地区，如：中国、美国、日本等")] string country = "中国",
        [Description("最小市值，单位亿美元")] decimal? minMarketCap = null,
        [Description("最大市值，单位亿美元")] decimal? maxMarketCap = null,
        [Description("最小市盈率PE")] decimal? minPE = null,
        [Description("最大市盈率PE")] decimal? maxPE = null,
        [Description("最小股息率，百分比")] decimal? minDividend = null,
        [Description("最大股息率，百分比")] decimal? maxDividend = null,
        [Description("行业筛选，如：科技、金融、医疗等")] string? sector = null,
        [Description("股票类型，如：大盘股、小盘股等")] string? stockType = null,
        [Description("返回数量限制")] int limit = 20)
    {
        try
        {
            _logger.LogInformation("开始从investing.com筛选股票，国家: {Country}, 限制: {Limit}", country, limit);

            return await _playwrightService.ExecuteWithPageAsync(async page =>
            {
                await page.GotoAsync(INVESTING_SCREENER_URL, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 30000
                });

                // 等待页面加载完成
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                await Task.Delay(2000); // 等待动态内容加载

                // 设置筛选条件
                await SetScreeningCriteria(page, country, minMarketCap, maxMarketCap,
                    minPE, maxPE, minDividend, maxDividend, sector, stockType);

                // 获取股票列表
                var stocks = await ExtractStockList(page, limit);

                _logger.LogInformation("成功获取 {Count} 只股票", stocks.Count);
                return stocks;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "筛选股票时发生错误");
            throw new InvalidOperationException($"筛选股票失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 快速筛选热门股票
    /// </summary>
    [KernelFunction("screen_hot_stocks"), Description("快速获取热门股票列表")]
    public async Task<List<InvestingStockInfo>> ScreenHotStocksAsync(
        [Description("国家/地区")] string country = "中国",
        [Description("排序方式：涨幅、成交量、市值等")] string sortBy = "涨幅",
        [Description("返回数量")] int limit = 20)
    {
        try
        {
            _logger.LogInformation("获取热门股票，国家: {Country}, 排序: {SortBy}", country, sortBy);

            return await _playwrightService.ExecuteWithPageAsync(async page =>
            {
                await page.GotoAsync(INVESTING_SCREENER_URL, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 30000
                });

                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                await Task.Delay(2000);

                // 设置国家筛选
                await SetCountryFilter(page, country);

                // 设置排序
                await SetSortingOrder(page, sortBy);

                // 获取股票列表
                var stocks = await ExtractStockList(page, limit);

                _logger.LogInformation("成功获取 {Count} 只热门股票", stocks.Count);
                return stocks;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取热门股票时发生错误");
            throw new InvalidOperationException($"获取热门股票失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 根据行业筛选股票
    /// </summary>
    [KernelFunction("screen_stocks_by_sector"), Description("根据指定行业筛选股票")]
    public async Task<List<InvestingStockInfo>> ScreenStocksBySectorAsync(
        [Description("行业名称，如：科技、金融、医疗、能源等")] string sector,
        [Description("国家/地区")] string country = "中国",
        [Description("返回数量")] int limit = 30)
    {
        try
        {
            _logger.LogInformation("按行业筛选股票，行业: {Sector}, 国家: {Country}", sector, country);

            return await _playwrightService.ExecuteWithPageAsync(async page =>
            {
                await page.GotoAsync(INVESTING_SCREENER_URL, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 30000
                });

                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                await Task.Delay(2000);

                // 设置国家和行业筛选
                await SetCountryFilter(page, country);
                await SetSectorFilter(page, sector);

                // 获取股票列表
                var stocks = await ExtractStockList(page, limit);

                _logger.LogInformation("成功获取 {Count} 只 {Sector} 行业股票", stocks.Count, sector);
                return stocks;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "按行业筛选股票时发生错误");
            throw new InvalidOperationException($"按行业筛选股票失败: {ex.Message}", ex);
        }
    }

    #region 私有方法

    /// <summary>
    /// 设置筛选条件
    /// </summary>
    private async Task SetScreeningCriteria(IPage page, string country, decimal? minMarketCap, decimal? maxMarketCap,
        decimal? minPE, decimal? maxPE, decimal? minDividend, decimal? maxDividend, string? sector, string? stockType)
    {
        try
        {
            // 设置国家筛选
            await SetCountryFilter(page, country);

            // 设置市值范围
            if (minMarketCap.HasValue || maxMarketCap.HasValue)
            {
                await SetMarketCapFilter(page, minMarketCap, maxMarketCap);
            }

            // 设置市盈率范围
            if (minPE.HasValue || maxPE.HasValue)
            {
                await SetPEFilter(page, minPE, maxPE);
            }

            // 设置股息率范围
            if (minDividend.HasValue || maxDividend.HasValue)
            {
                await SetDividendFilter(page, minDividend, maxDividend);
            }

            // 设置行业筛选
            if (!string.IsNullOrEmpty(sector))
            {
                await SetSectorFilter(page, sector);
            }

            // 设置股票类型
            if (!string.IsNullOrEmpty(stockType))
            {
                await SetStockTypeFilter(page, stockType);
            }

            // 等待筛选结果加载
            await Task.Delay(3000);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "设置筛选条件时发生警告");
        }
    }

    /// <summary>
    /// 设置国家筛选
    /// </summary>
    private async Task SetCountryFilter(IPage page, string country)
    {
        try
        {
            // 查找国家选择器并设置
            var countrySelector = "select[name='country'], .country-filter, [data-test='country-select']";
            var element = await page.QuerySelectorAsync(countrySelector);

            if (element != null)
            {
                await element.SelectOptionAsync(new[] { country });
                await Task.Delay(1000);
            }
            else
            {
                _logger.LogWarning("未找到国家筛选器");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "设置国家筛选时发生错误: {Country}", country);
        }
    }

    /// <summary>
    /// 设置市值筛选
    /// </summary>
    private async Task SetMarketCapFilter(IPage page, decimal? minMarketCap, decimal? maxMarketCap)
    {
        try
        {
            if (minMarketCap.HasValue)
            {
                var minInput = await page.QuerySelectorAsync("input[name='minMarketCap'], .market-cap-min");
                if (minInput != null)
                {
                    await minInput.FillAsync(minMarketCap.Value.ToString());
                }
            }

            if (maxMarketCap.HasValue)
            {
                var maxInput = await page.QuerySelectorAsync("input[name='maxMarketCap'], .market-cap-max");
                if (maxInput != null)
                {
                    await maxInput.FillAsync(maxMarketCap.Value.ToString());
                }
            }

            await Task.Delay(500);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "设置市值筛选时发生错误");
        }
    }

    /// <summary>
    /// 设置市盈率筛选
    /// </summary>
    private async Task SetPEFilter(IPage page, decimal? minPE, decimal? maxPE)
    {
        try
        {
            if (minPE.HasValue)
            {
                var minInput = await page.QuerySelectorAsync("input[name='minPE'], .pe-min");
                if (minInput != null)
                {
                    await minInput.FillAsync(minPE.Value.ToString());
                }
            }

            if (maxPE.HasValue)
            {
                var maxInput = await page.QuerySelectorAsync("input[name='maxPE'], .pe-max");
                if (maxInput != null)
                {
                    await maxInput.FillAsync(maxPE.Value.ToString());
                }
            }

            await Task.Delay(500);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "设置市盈率筛选时发生错误");
        }
    }

    /// <summary>
    /// 设置股息率筛选
    /// </summary>
    private async Task SetDividendFilter(IPage page, decimal? minDividend, decimal? maxDividend)
    {
        try
        {
            if (minDividend.HasValue)
            {
                var minInput = await page.QuerySelectorAsync("input[name='minDividend'], .dividend-min");
                if (minInput != null)
                {
                    await minInput.FillAsync(minDividend.Value.ToString());
                }
            }

            if (maxDividend.HasValue)
            {
                var maxInput = await page.QuerySelectorAsync("input[name='maxDividend'], .dividend-max");
                if (maxInput != null)
                {
                    await maxInput.FillAsync(maxDividend.Value.ToString());
                }
            }

            await Task.Delay(500);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "设置股息率筛选时发生错误");
        }
    }

    /// <summary>
    /// 设置行业筛选
    /// </summary>
    private async Task SetSectorFilter(IPage page, string sector)
    {
        try
        {
            var sectorSelector = "select[name='sector'], .sector-filter, [data-test='sector-select']";
            var element = await page.QuerySelectorAsync(sectorSelector);

            if (element != null)
            {
                await element.SelectOptionAsync(new[] { sector });
                await Task.Delay(1000);
            }
            else
            {
                _logger.LogWarning("未找到行业筛选器");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "设置行业筛选时发生错误: {Sector}", sector);
        }
    }

    /// <summary>
    /// 设置股票类型筛选
    /// </summary>
    private async Task SetStockTypeFilter(IPage page, string stockType)
    {
        try
        {
            var typeSelector = "select[name='stockType'], .stock-type-filter";
            var element = await page.QuerySelectorAsync(typeSelector);

            if (element != null)
            {
                await element.SelectOptionAsync(new[] { stockType });
                await Task.Delay(1000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "设置股票类型筛选时发生错误: {StockType}", stockType);
        }
    }

    /// <summary>
    /// 设置排序方式
    /// </summary>
    private async Task SetSortingOrder(IPage page, string sortBy)
    {
        try
        {
            var sortMapping = new Dictionary<string, string>
            {
                { "涨幅", "change" },
                { "成交量", "volume" },
                { "市值", "market_cap" },
                { "价格", "price" },
                { "市盈率", "pe_ratio" }
            };

            if (sortMapping.TryGetValue(sortBy, out var sortValue))
            {
                var sortSelector = $"[data-sort='{sortValue}'], .sort-{sortValue}";
                var element = await page.QuerySelectorAsync(sortSelector);

                if (element != null)
                {
                    await element.ClickAsync();
                    await Task.Delay(2000);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "设置排序方式时发生错误: {SortBy}", sortBy);
        }
    }

    /// <summary>
    /// 提取股票列表
    /// </summary>
    private async Task<List<InvestingStockInfo>> ExtractStockList(IPage page, int limit)
    {
        var stocks = new List<InvestingStockInfo>();

        try
        {
            // 等待表格加载
            await page.WaitForSelectorAsync("table, .stock-table, .screener-table", new PageWaitForSelectorOptions
            {
                Timeout = 10000
            });

            // 多种可能的行选择器
            var rowSelectors = new[]
            {
                "table tbody tr",
                ".stock-table tbody tr",
                ".screener-table tbody tr",
                "[data-test='stock-row']",
                ".stock-row"
            };

            IReadOnlyList<IElementHandle> rows = null!;

            foreach (var selector in rowSelectors)
            {
                rows = await page.QuerySelectorAllAsync(selector);
                if (rows.Count > 0) break;
            }

            if (rows == null || rows.Count == 0)
            {
                _logger.LogWarning("未找到股票数据行");
                return stocks;
            }

            var processedCount = 0;
            foreach (var row in rows.Take(limit))
            {
                try
                {
                    var stock = await ExtractStockInfoFromRow(row);
                    if (stock != null)
                    {
                        stocks.Add(stock);
                        processedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "提取第 {Index} 行股票信息时发生错误", processedCount + 1);
                }
            }

            _logger.LogInformation("成功提取 {Count} 只股票信息", stocks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "提取股票列表时发生错误");
        }

        return stocks;
    }

    /// <summary>
    /// 从表格行提取股票信息
    /// </summary>
    private async Task<InvestingStockInfo?> ExtractStockInfoFromRow(IElementHandle row)
    {
        try
        {
            var cells = await row.QuerySelectorAllAsync("td");
            if (cells.Count < 3) return null;

            var stock = new InvestingStockInfo();

            // 提取股票名称和代码（通常在第一列）
            var nameCell = cells[0];
            var nameText = await nameCell.InnerTextAsync();

            // 尝试分离股票名称和代码
            var nameMatch = Regex.Match(nameText, @"(.+?)\s*\(([^)]+)\)");
            if (nameMatch.Success)
            {
                stock.Name = nameMatch.Groups[1].Value.Trim();
                stock.Symbol = nameMatch.Groups[2].Value.Trim();
            }
            else
            {
                stock.Name = nameText.Trim();
                stock.Symbol = nameText.Trim();
            }

            // 提取其他信息（价格、涨跌幅等）
            if (cells.Count > 1)
            {
                var priceText = await cells[1].InnerTextAsync();
                if (decimal.TryParse(priceText.Replace(",", "").Replace("$", ""), out var price))
                {
                    stock.Price = price;
                }
            }

            if (cells.Count > 2)
            {
                var changeText = await cells[2].InnerTextAsync();
                var changeMatch = Regex.Match(changeText, @"([+-]?\d+\.?\d*)");
                if (changeMatch.Success && decimal.TryParse(changeMatch.Groups[1].Value, out var change))
                {
                    stock.ChangePercent = change;
                }
            }

            // 提取更多列的数据
            await ExtractAdditionalStockData(stock, cells);

            return stock;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "从行提取股票信息时发生错误");
            return null;
        }
    }

    /// <summary>
    /// 提取额外的股票数据
    /// </summary>
    private async Task ExtractAdditionalStockData(InvestingStockInfo stock, IReadOnlyList<IElementHandle> cells)
    {
        try
        {
            // 根据实际的表格结构提取更多数据
            for (int i = 3; i < Math.Min(cells.Count, 10); i++)
            {
                var cellText = await cells[i].InnerTextAsync();

                // 尝试识别不同类型的数据
                if (decimal.TryParse(cellText.Replace(",", "").Replace("$", "").Replace("%", ""), out var value))
                {
                    switch (i)
                    {
                        case 3: // 可能是市值
                            stock.MarketCap = value;
                            break;
                        case 4: // 可能是PE
                            stock.PERatio = value;
                            break;
                        case 5: // 可能是股息率
                            stock.DividendYield = value;
                            break;
                        case 6: // 可能是成交量
                            stock.Volume = value;
                            break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "提取额外股票数据时发生错误");
        }
    }

    #endregion
}
