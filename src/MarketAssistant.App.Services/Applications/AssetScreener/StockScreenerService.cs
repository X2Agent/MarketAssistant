using System.Globalization;
using System.Net;
using MarketAssistant.Applications.AssetScreener.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.AssetScreener;

/// <summary>
/// 雪球网股票筛选服务（基于 HTTP API）
/// API 端点: GET https://xueqiu.com/service/screener/screen
/// </summary>
public sealed class StockScreenerService : IAssetScreenerService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CookieContainer _cookieContainer;
    private readonly ILogger<StockScreenerService> _logger;

    private const string ScreenerEndpoint = "/service/screener/screen";

    /// <summary>
    /// 需要拼接报告期日期后缀的财务指标（格式：field.YYYYMMDD=min_max）
    /// </summary>
    private static readonly HashSet<string> Adj1Fields = new(StringComparer.OrdinalIgnoreCase)
    {
        "roediluted", "eps", "bps", "npay", "netprofit",
        "total_revenue", "oiy", "niota"
    };

    /// <summary>
    /// 标识字段和元数据字段，动态解析时跳过（不放入 Indicators 字典）。
    /// 其余数值字段全部入字典，由 StockDataFormatter 统一做字段名映射和单位转换。
    /// </summary>
    private static readonly HashSet<string> s_handledFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "symbol",
        "exchange", "type", "tick_size", "has_follow", "indcode", "areacode"
    };

    private static readonly Dictionary<IndustryType, string> IndustryCodeMap = new()
    {
        { IndustryType.ComputerEquipment, "S7101" },
        { IndustryType.SoftwareDevelopment, "S7104" },
        { IndustryType.Semiconductor, "S2701" },
        { IndustryType.Battery, "S6307" },
        { IndustryType.PhotovoltaicEquipment, "S6305" },
        { IndustryType.WindPowerEquipment, "S6306" },
        { IndustryType.ChemicalPharmaceutical, "S3701" },
        { IndustryType.BiologicalProducts, "S3703" },
        { IndustryType.MedicalDevices, "S3705" },
        { IndustryType.Liquor, "S3405" },
        { IndustryType.BeveragesDairy, "S3407" },
        { IndustryType.FoodProcessing, "S3404" },
        { IndustryType.JointStockBank, "S4803" },
        { IndustryType.StateBanks, "S4802" },
        { IndustryType.RealEstateDevelopment, "S4301" },
        { IndustryType.PassengerVehicles, "S2805" },
        { IndustryType.AutoParts, "S2802" },
        { IndustryType.CommunicationEquipment, "S7302" },
        { IndustryType.CommunicationServices, "S7301" },
        { IndustryType.Power, "S4101" },
        { IndustryType.ChemicalMaterials, "S2202" },
        { IndustryType.ChemicalProducts, "S2203" },
        { IndustryType.ConstructionMachinery, "S6406" },
        { IndustryType.SpecializedEquipment, "S6402" },
        { IndustryType.WhiteAppliances, "S3301" },
        { IndustryType.SmallAppliances, "S3303" }
    };

    public StockScreenerService(
        IHttpClientFactory httpClientFactory,
        CookieContainer xueqiuCookieContainer,
        ILogger<StockScreenerService> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _cookieContainer = xueqiuCookieContainer ?? throw new ArgumentNullException(nameof(xueqiuCookieContainer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<ScreenerAssetInfo>> ScreenAsync(object criteria)
    {
        if (criteria is not StockCriteria stockCriteria)
        {
            throw new ArgumentException("筛选条件类型错误，期望 StockCriteria", nameof(criteria));
        }

        _logger.LogInformation("开始雪球选股，条件数量: {Count}, 限制: {Limit}",
            stockCriteria.Criteria.Count, stockCriteria.Limit);

        try
        {
            await EnsureCookiesAsync();

            var queryParams = BuildQueryParams(stockCriteria);
            var stocks = await FetchFromXueqiuAsync(queryParams);

            _logger.LogInformation("雪球选股完成，结果数量: {Count}", stocks.Count);
            return stocks.Cast<ScreenerAssetInfo>().ToList();
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            _logger.LogError(ex, "雪球选股过程中发生错误");
            throw new FriendlyException($"筛选股票失败: {ex.Message}", ex);
        }
    }

    private async Task EnsureCookiesAsync()
    {
        var cookies = _cookieContainer.GetCookies(new Uri("https://xueqiu.com"));
        if (cookies.Count > 0)
        {
            return;
        }

        _logger.LogDebug("雪球 Cookie 为空，访问首页获取 Cookie");
        using var client = _httpClientFactory.CreateClient("Xueqiu");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        using var response = await client.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("访问雪球首页获取 Cookie 失败，HTTP 状态码: {StatusCode}", (int)response.StatusCode);
            throw new FriendlyException($"无法连接雪球网（HTTP {(int)response.StatusCode}），请检查网络连接后重试");
        }

        var cookiesAfter = _cookieContainer.GetCookies(new Uri("https://xueqiu.com"));
        if (cookiesAfter.Count == 0)
        {
            _logger.LogWarning("访问雪球首页成功但未获取到 Cookie，可能被反爬机制拦截");
            throw new FriendlyException("雪球网未返回有效的 Cookie，可能被反爬机制拦截，请稍后重试");
        }
    }

    /// <summary>
    /// 获取最新可用财报报告期日期（YYYYMMDD 格式）。
    /// 当前日期减 30 天，取最近一个已结束的季度末。
    /// </summary>
    private static string GetLatestReportingPeriod()
    {
        var referenceDate = DateTime.Now.AddDays(-30);
        var year = referenceDate.Year;
        var month = referenceDate.Month;

        var quarterEndMonth = month switch
        {
            >= 10 => 9,
            >= 7 => 6,
            >= 4 => 3,
            _ => 12
        };

        if (quarterEndMonth == 12)
        {
            year--;
        }

        var day = quarterEndMonth switch
        {
            3 => "0331",
            6 => "0630",
            9 => "0930",
            _ => "1231"
        };

        return $"{year}{day}";
    }

    private string BuildQueryParams(StockCriteria criteria)
    {
        var exchange = criteria.Market switch
        {
            AShareType.ShanghaiAShares => "sha",
            AShareType.ShenzhenAShares => "sza",
            _ => "sh_sz"
        };

        var indcode = "";
        if (criteria.Industry != IndustryType.All &&
            IndustryCodeMap.TryGetValue(criteria.Industry, out var code))
        {
            indcode = code;
        }

        var reportingPeriod = GetLatestReportingPeriod();

        var filterParts = new List<string>();
        foreach (var condition in criteria.Criteria)
        {
            var min = condition.MinValue?.ToString(CultureInfo.InvariantCulture) ?? "";
            var max = condition.MaxValue?.ToString(CultureInfo.InvariantCulture) ?? "";
            var filterValue = $"{min}_{max}";

            var fieldKey = Adj1Fields.Contains(condition.Code)
                ? $"{condition.Code}.{reportingPeriod}"
                : condition.Code;

            filterParts.Add($"{fieldKey}={filterValue}");
        }

        var orderField = criteria.Criteria.Count > 0 ? criteria.Criteria[0].Code : "mc";
        var orderByParam = Adj1Fields.Contains(orderField)
            ? $"{orderField}.{reportingPeriod}"
            : orderField;

        // 确保 mc（总市值）始终包含在查询中，使响应包含市值数据
        var hasMc = orderByParam.Equals("mc", StringComparison.OrdinalIgnoreCase) ||
                    criteria.Criteria.Any(c => c.Code.Equals("mc", StringComparison.OrdinalIgnoreCase));
        if (!hasMc)
        {
            filterParts.Add("mc=0_99999999999999");
        }

        var filterString = filterParts.Count > 0 ? "&" + string.Join("&", filterParts) : "";

        return $"category=CN&exchange={exchange}&indcode={indcode}" +
               $"&order_by={orderByParam}&order=desc&page=1&size={criteria.Limit}&only_count=0" +
               filterString;
    }

    private async Task<List<ScreenerStockInfo>> FetchFromXueqiuAsync(string queryParams)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var url = $"{ScreenerEndpoint}?{queryParams}&_={timestamp}";

        _logger.LogDebug("调用雪球选股 API: {Url}", url);

        using var client = _httpClientFactory.CreateClient("Xueqiu");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await client.SendAsync(request);

        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("雪球选股 API 返回错误，状态码: {StatusCode}，响应: {Body}",
                (int)response.StatusCode,
                json.Length > 500 ? json[..500] : json);
            throw new FriendlyException($"雪球网选股接口返回错误（HTTP {(int)response.StatusCode}），请稍后重试");
        }

        return ParseResponse(json);
    }

    private List<ScreenerStockInfo> ParseResponse(string json)
    {
        var stocks = new List<ScreenerStockInfo>();

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            _logger.LogError("雪球 API 返回非 JSON 格式数据（可能是反爬验证页面），响应前 500 字符: {Body}",
                json.Length > 500 ? json[..500] : json);
            throw new FriendlyException("雪球网返回了非预期的数据格式，可能是反爬验证页面，请稍后重试");
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("list", out var list) ||
                list.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("雪球 API 返回数据格式异常，响应前 500 字符: {Body}",
                    json.Length > 500 ? json[..500] : json);
                return stocks;
            }

            foreach (var item in list.EnumerateArray())
            {
                var stock = ParseStockItem(item);
                if (stock != null)
                {
                    stocks.Add(stock);
                }
            }
        }

        return stocks;
    }

    private static ScreenerStockInfo? ParseStockItem(JsonElement item)
    {
        var name = GetString(item, "name");
        var symbol = GetString(item, "symbol");

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(symbol))
        {
            return null;
        }

        var stock = new ScreenerStockInfo { Name = name, Symbol = symbol };

        // 所有数值字段统一放入 Indicators 字典
        foreach (var prop in item.EnumerateObject())
        {
            if (s_handledFields.Contains(prop.Name)) continue;
            if (prop.Value.ValueKind is JsonValueKind.Number or JsonValueKind.String)
            {
                stock.Indicators[prop.Name] = GetDecimal(item, prop.Name);
            }
        }

        // 回填基类属性，保持 ScreenerAssetInfo 接口契约
        if (stock.Indicators.TryGetValue("current", out var current)) stock.Current = current;
        if (stock.Indicators.TryGetValue("pct", out var pct)) stock.Pct = pct;
        if (stock.Indicators.TryGetValue("amount", out var amount)) stock.Amount = amount;
        if (stock.Indicators.TryGetValue("mc", out var mc)) stock.Mc = mc;
        if (stock.Indicators.TryGetValue("fmc", out var fmc)) stock.Fmc = fmc;
        if (stock.Indicators.TryGetValue("volume", out var volume)) stock.Volume = volume;

        return stock;
    }

    private static string GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static decimal GetDecimal(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return 0;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(
                value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d,
            _ => 0
        };
    }
}
