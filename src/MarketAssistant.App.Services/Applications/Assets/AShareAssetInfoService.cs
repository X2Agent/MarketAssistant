using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace MarketAssistant.Applications.Assets;

/// <summary>
/// A股资产信息服务实现
/// </summary>
public class AShareAssetInfoService : IAssetInfoService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AShareAssetInfoService> _logger;

    public AShareAssetInfoService(
        IHttpClientFactory httpClientFactory,
        ILogger<AShareAssetInfoService> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger;
    }

    public async Task<List<(string Name, string Code)>> SearchAsync(string keyword, CancellationToken cancellationToken = default)
    {
        // cls.cn 搜索 JSON API，直接 POST 即可，无需 Playwright
        const string apiUrl = "https://www.cls.cn/api/sw?app=CailianpressWeb&os=web&sv=8.7.9&sign=b02d8f7bc4c45eeb3e86904203597da2";

        var body = new
        {
            type = "stock",
            keyword = keyword.Trim(),
            rn = 20,
            page = 0,
            os = "web",
            sv = "8.7.9",
            app = "CailianpressWeb"
        };

        try
        {
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            using var content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");

            var response = await httpClient.PostAsync(apiUrl, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(json);

            var stockList = new List<(string Name, string Code)>();

            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.TryGetProperty("stock", out var stock) &&
                stock.TryGetProperty("data", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    var title = item.GetProperty("title").GetString() ?? "";
                    var stockId = item.GetProperty("stock_id").GetString() ?? "";

                    // 去除 <em> 高亮标签
                    title = title.Replace("<em>", "").Replace("</em>", "").Trim();

                    if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(stockId))
                        stockList.Add((title, stockId));
                }
            }

            return stockList;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "搜索股票失败，返回空结果");
            return [];
        }
    }

    public async Task<AssetInfo> GetAssetInfoAsync(string code, string market = "", CancellationToken cancellationToken = default)
    {
        var assetInfo = new AssetInfo
        {
            Code = code,
            Name = "未知股票",
            Market = market,
            MarketType = MarketType.AShare
        };

        try
        {
            var fullCode = string.IsNullOrEmpty(market) ? code : $"{market}{code}";
            var clsCode = StockSymbolConverter.ToClsFormat(fullCode);
            if (string.IsNullOrEmpty(clsCode))
                return assetInfo;

            var url = $"/quote/stock/basic?secu_code={clsCode}&fields=secu_name,secu_code,last_px,change&app=CailianpressWeb&os=web&sv=8.4.6";

            using var httpClient = _httpClientFactory.CreateClient("Cls");
            var response = await httpClient.GetStringAsync(url, cancellationToken);
            using var jsonDocument = JsonDocument.Parse(response);

            if (!jsonDocument.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
                return assetInfo;

            // 股票名称
            if (data.TryGetProperty("secu_name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                assetInfo.Name = nameEl.GetString()?.Trim() ?? "未知股票";

            // 股票代码 & 市场
            if (data.TryGetProperty("secu_code", out var codeEl) && codeEl.ValueKind == JsonValueKind.String)
            {
                var rawCode = codeEl.GetString()?.Trim() ?? "";
                if (rawCode.StartsWith("SH", StringComparison.OrdinalIgnoreCase))
                {
                    assetInfo.Market = "SH";
                    assetInfo.Code = rawCode[2..];
                }
                else if (rawCode.StartsWith("SZ", StringComparison.OrdinalIgnoreCase))
                {
                    assetInfo.Market = "SZ";
                    assetInfo.Code = rawCode[2..];
                }
                else
                {
                    assetInfo.Code = rawCode;
                }
            }

            // 当前价格
            if (data.TryGetProperty("last_px", out var priceEl))
                assetInfo.CurrentPrice = priceEl.ToString();

            // 涨跌幅
            if (data.TryGetProperty("change", out var changeEl))
            {
                var changeText = changeEl.ToString();
                if (!string.IsNullOrEmpty(changeText))
                    assetInfo.ChangePercentage = changeText.Contains('%') ? changeText : $"{changeText}%";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取股票详细数据异常: {Code}", code);
        }

        return assetInfo;
    }

    public async Task<List<HotAsset>> GetHotAssetsAsync()
    {
        try
        {
            // 东方财富主力资金排行 API：GET 请求、无需认证、包含名称/价格/涨跌幅/资金流入
            var url = "https://push2.eastmoney.com/api/qt/clist/get?pn=1&pz=8&po=1&np=1&fltt=2&invt=2&fid=f62" +
                      "&fs=m:0+t:6,m:0+t:80,m:1+t:2,m:1+t:23,m:0+t:81+s:2048" +
                      "&fields=f2,f3,f12,f13,f14,f62";

            using var httpClient = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

            using var response = await httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var jsonDocument = JsonDocument.Parse(json);
            var root = jsonDocument.RootElement;

            if (!root.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("diff", out var diff) ||
                diff.ValueKind != JsonValueKind.Array)
            {
                _logger.LogError("GetHotAssetsAsync: 东方财富API返回数据格式异常");
                return [];
            }

            var hotAssets = new List<HotAsset>();

            foreach (var item in diff.EnumerateArray())
            {
                var code = item.TryGetProperty("f12", out var codeEl) ? codeEl.GetString() ?? "" : "";
                var marketId = item.TryGetProperty("f13", out var mktEl) ? mktEl.GetInt32() : 0;
                var market = marketId == 1 ? "SH" : "SZ";

                var changeRaw = item.TryGetProperty("f3", out var changeEl) ? changeEl.GetDouble() : 0;
                var flowRaw = item.TryGetProperty("f62", out var flowEl) ? flowEl.GetDouble() : 0;

                hotAssets.Add(new HotAsset
                {
                    Name = item.TryGetProperty("f14", out var nameEl) ? nameEl.GetString() ?? "" : "",
                    Code = code,
                    Market = market,
                    CurrentPrice = item.TryGetProperty("f2", out var priceEl) ? priceEl.GetDouble().ToString("F2") : "",
                    ChangePercentage = $"{changeRaw:+0.00;-0.00;0.00}%",
                    MetricLabel = "净流入",
                    MetricValue = flowRaw.ToString("F0"),
                    MarketType = MarketType.AShare
                });
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

