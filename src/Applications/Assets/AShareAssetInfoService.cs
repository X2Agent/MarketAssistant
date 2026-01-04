using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Browser;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using System.Text.Json;

namespace MarketAssistant.Applications.Assets;

/// <summary>
/// A股资产信息服务实现
/// </summary>
public class AShareAssetInfoService : IAssetInfoService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AShareAssetInfoService> _logger;
    private readonly PlaywrightService _playwrightService;

    public AShareAssetInfoService(ILogger<AShareAssetInfoService> logger, PlaywrightService playwrightService)
    {
        _httpClient = new HttpClient();
        _logger = logger;
        _playwrightService = playwrightService;
    }

    public async Task<List<(string Name, string Code)>> SearchAsync(string keyword, CancellationToken cancellationToken = default)
    {
        var url = $"https://www.cls.cn/searchPage?keyword={keyword.Trim()}&type=stock";

        return await _playwrightService.ExecuteWithPageAsync(async page =>
        {
            var stockList = new List<(string Name, string Code)>();

            try
            {
                await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                await page.WaitForSelectorAsync(".search-stock-list", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 15000 });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "搜索页打开失败或超时，返回空结果");
                return stockList;
            }
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

    public async Task<AssetInfo> GetAssetInfoAsync(string code, string market = "", CancellationToken cancellationToken = default)
    {
        // 创建资产信息对象
        var assetInfo = new AssetInfo
        {
            Code = code,
            Name = "未知股票",
            Market = market,
            MarketType = MarketType.AShare
        };

        try
        {
            // 构建股票详情页URL
            var fullCode = string.IsNullOrEmpty(market) ? code : $"{market}{code}".ToLower();
            var url = $"https://www.cls.cn/stock?code={fullCode}";

            assetInfo = await _playwrightService.ExecuteWithPageAsync(async page =>
            {
                try
                {
                    await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                    await page.WaitForSelectorAsync(".stock-detail", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 15000 });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "股票详情页打开失败或超时，降级返回已有字段");
                    return assetInfo;
                }

                var stockDetailElement = await page.QuerySelectorAsync(".stock-detail");
                if (stockDetailElement != null)
                {
                    var nameElement = await stockDetailElement.QuerySelectorAsync(".f-s-25.f-w-b");
                    if (nameElement != null)
                    {
                        assetInfo.Name = (await nameElement.InnerTextAsync()).Trim();
                    }

                    var codeElement = await stockDetailElement.QuerySelectorAsync(".f-s-20.f-w-b");
                    if (codeElement != null)
                    {
                        var fullCodeText = (await codeElement.InnerTextAsync()).Trim();
                        if (!string.IsNullOrEmpty(fullCodeText))
                        {
                            if (fullCodeText.StartsWith("sh", StringComparison.OrdinalIgnoreCase))
                            {
                                assetInfo.Market = "SH";
                                assetInfo.Code = fullCodeText.Substring(2);
                            }
                            else if (fullCodeText.StartsWith("sz", StringComparison.OrdinalIgnoreCase))
                            {
                                assetInfo.Market = "SZ";
                                assetInfo.Code = fullCodeText.Substring(2);
                            }
                            else
                            {
                                assetInfo.Code = fullCodeText;
                            }
                        }
                    }
                }

                var quoteChangeBox = await page.QuerySelectorAsync(".quote-change-box");
                if (quoteChangeBox != null)
                {
                    var priceElement = await quoteChangeBox.QuerySelectorAsync(".quote-price");
                    if (priceElement != null)
                    {
                        assetInfo.CurrentPrice = (await priceElement.InnerTextAsync()).Trim();
                    }

                    var changeElement = await quoteChangeBox.QuerySelectorAsync(".quote-change");
                    if (changeElement != null)
                    {
                        var changeText = (await changeElement.InnerTextAsync()).Trim();
                        if (changeText.Contains("%"))
                        {
                            var startIndex = changeText.IndexOf("(") + 1;
                            var endIndex = changeText.IndexOf("%") + 1;
                            if (startIndex > 0 && endIndex > startIndex)
                            {
                                assetInfo.ChangePercentage = changeText.Substring(startIndex, endIndex - startIndex);
                            }
                            else
                            {
                                assetInfo.ChangePercentage = changeText;
                            }
                        }
                        else
                        {
                            assetInfo.ChangePercentage = changeText;
                        }
                    }
                }

                var stockRelatedBox = await page.QuerySelectorAsync(".stock-related-box");
                if (stockRelatedBox != null)
                {
                    var stockPlage = await stockRelatedBox.QuerySelectorAsync(".stock-related-plate");
                    if (stockPlage != null)
                    {
                        var sectorElement = await stockPlage.QuerySelectorAsync(".m-r-10.f-s-20.c-222.f-w-b");
                        if (sectorElement != null)
                        {
                            assetInfo.SectorName = (await sectorElement.InnerTextAsync()).Trim();
                        }
                    }
                }

                return assetInfo;
            }, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"获取股票详细数据异常: {ex.Message}");
        }

        return assetInfo;
    }

    public async Task<List<HotAsset>> GetHotAssetsAsync()
    {
        try
        {
            DateTime today = DateTime.Now;

            if (today.DayOfWeek == DayOfWeek.Saturday)
            {
                today = today.AddDays(-1);
            }
            else if (today.DayOfWeek == DayOfWeek.Sunday)
            {
                today = today.AddDays(-2);
            }

            string formattedDate = today.ToString("yyyyMMdd");
            var url = $"https://finance.pae.baidu.com/vapi/v1/hotrank?product=stock&day={formattedDate}&pn=0&rn=8&market=ab&type=day&finClientType=pc";

            var response = await _httpClient.GetStringAsync(url);
            var jsonDocument = JsonDocument.Parse(response);
            var root = jsonDocument.RootElement;

            if (!root.TryGetProperty("Result", out var resultElement))
            {
                _logger.LogError("GetHotAssetsAsync: API返回数据格式不正确，缺少Result字段");
                return new List<HotAsset>();
            }

            if (!resultElement.TryGetProperty("header", out var headerElement) ||
                !resultElement.TryGetProperty("body", out var bodyElement))
            {
                _logger.LogError("GetHotAssetsAsync: API返回数据格式不正确，缺少header或body字段");
                return new List<HotAsset>();
            }

            var headerIndices = new Dictionary<string, int>();
            int index = 0;
            foreach (var header in headerElement.EnumerateArray())
            {
                headerIndices[header.GetString() ?? string.Empty] = index++;
            }

            var hotAssets = new List<HotAsset>();

            foreach (var stockArray in bodyElement.EnumerateArray())
            {
                if (stockArray.GetArrayLength() != headerIndices.Count)
                {
                    _logger.LogError("GetHotAssetsAsync: 股票数据数组长度与header不匹配");
                    continue;
                }

                var stockData = stockArray.EnumerateArray().ToArray();

                var hotAsset = new HotAsset
                {
                    Name = stockData[headerIndices["股票名称"]].GetString() ?? string.Empty,
                    ChangePercentage = stockData[headerIndices["涨跌幅"]].GetString() ?? string.Empty,
                    SectorName = stockData[headerIndices["所属板块名称"]].GetString() ?? string.Empty,
                    Code = stockData[headerIndices["市场代码"]].GetString() ?? string.Empty,
                    CurrentPrice = stockData[headerIndices["现价"]].GetString() ?? string.Empty,
                    Market = stockData[headerIndices["市场缩写"]].GetString() ?? string.Empty,
                    RankChange = stockData[headerIndices["排名变化"]].GetString() ?? string.Empty,
                    HeatIndex = stockData[headerIndices["综合热度"]].GetString() ?? string.Empty,
                    MarketType = MarketType.AShare
                };

                hotAssets.Add(hotAsset);
            }

            return hotAssets;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetHotAssetsAsync未知异常: {Message}", ex.Message);
            throw new Infrastructure.Core.FriendlyException($"获取热门股票失败: {ex.Message}", ex);
        }
    }
}

