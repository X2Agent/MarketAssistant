using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Logging;
using System.Net;
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
            using var httpClient = _httpClientFactory.CreateClient("Cls");
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
        // 新浪财经个股资金流排行 API：按净流入降序，返回 symbol/name/trade/changeratio/netamount 等
        // 原 push2.eastmoney.com 端点在部分网络环境下 TLS 重协商被中断（"The response ended prematurely"），
        // 新浪接口稳定且响应更快，直接作为唯一数据源。
        // 注意：新浪接口返回 Content-Type: application/json; charset=gbk，
        // .NET 默认不支持 GBK 编码，需注册 CodePagesEncodingProvider 并手动用 GBK 解码。
        var url = "/quotes_service/api/json_v2.php/MoneyFlow.ssl_bkzj_ssggzj?page=1&num=8&sort=netamount&asc=0";

        try
        {
            // 注册中文编码提供程序（幂等），使 GBK/GB2312 可用
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            using var httpClient = _httpClientFactory.CreateClient("SinaFinance");
            using var stream = await httpClient.GetStreamAsync(url);
            using var reader = new StreamReader(stream, System.Text.Encoding.GetEncoding("gbk"));
            var json = await reader.ReadToEndAsync();
            using var jsonDocument = JsonDocument.Parse(json);

            if (jsonDocument.RootElement.ValueKind != JsonValueKind.Array)
            {
                _logger.LogError("GetHotAssetsAsync: 新浪API返回数据格式异常");
                return [];
            }

            var hotAssets = new List<HotAsset>();

            foreach (var item in jsonDocument.RootElement.EnumerateArray())
            {
                var symbol = item.TryGetProperty("symbol", out var symEl) ? symEl.GetString() ?? "" : "";
                if (symbol.Length < 2)
                    continue;

                var market = symbol.StartsWith("sh", StringComparison.OrdinalIgnoreCase) ? "SH" :
                             symbol.StartsWith("sz", StringComparison.OrdinalIgnoreCase) ? "SZ" :
                             symbol.StartsWith("bj", StringComparison.OrdinalIgnoreCase) ? "BJ" : "";

                var code = symbol[2..];
                var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                var price = item.TryGetProperty("trade", out var tradeEl) ? tradeEl.GetString() ?? "" : "";
                // 新浪接口的 changeratio 和 netamount 均为字符串类型，需手动解析
                var changeRatio = ParseDouble(item, "changeratio");
                var netAmount = ParseDouble(item, "netamount");

                hotAssets.Add(new HotAsset
                {
                    Name = name,
                    Code = code,
                    Market = market,
                    CurrentPrice = price,
                    ChangePercentage = $"{changeRatio * 100:+0.00;-0.00;0.00}%",
                    MetricLabel = "净流入",
                    MetricValue = netAmount.ToString("F0"),
                    MarketType = MarketType.AShare
                });
            }

            return hotAssets;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetHotAssetsAsync: {Message}", ex.Message);
            throw new Infrastructure.Core.FriendlyException($"获取热门股票失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 从 JSON 元素解析 double 值，兼容字符串和数字两种类型。
    /// 新浪接口返回的数值字段多为字符串类型。
    /// </summary>
    private static double ParseDouble(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var element))
            return 0;

        return element.ValueKind switch
        {
            JsonValueKind.String => double.TryParse(element.GetString(), out var v) ? v : 0,
            JsonValueKind.Number => element.GetDouble(),
            _ => 0
        };
    }
}

