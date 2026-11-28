using System.Globalization;
using System.Net;
using MarketAssistant.Applications.AssetScreener.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.AssetScreener;

/// <summary>
/// 雪球网股票筛选服务（基于 HTTP API）
/// API 端点: GET https://xueqiu.com/service/screener/screen
/// </summary>
public sealed class StockScreenerService(
    IHttpClientFactory httpClientFactory,
    CookieContainer xueqiuCookieContainer,
    ILogger<StockScreenerService> logger) : IAssetScreenerService
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly CookieContainer _cookieContainer = xueqiuCookieContainer;
    private readonly ILogger<StockScreenerService> _logger = logger;

    private const string ScreenerEndpoint = "/service/screener/screen";

    /// <summary>
    /// 累计类财务指标（格式：field.YYYYMMDD=min_max）：数值按报告期逐季累加且未年化，取年报才与阈值口径一致
    /// </summary>
    private static readonly HashSet<string> CumulativeFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "roediluted", "eps", "bps", "netprofit", "total_revenue", "niota"
    };

    /// <summary>
    /// 同比增速类财务指标（格式：field.YYYYMMDD=min_max）：按披露截止日取最近一期已披露完的季报
    /// </summary>
    private static readonly HashSet<string> GrowthFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "npay", "oiy"
    };

    /// <summary>
    /// 第 3 步分析依赖的关键列：条件没涉及时用 <see cref="WideRange"/> 补列。
    /// 补列会剔除这些字段为空的股票（2026-11-28 实测全部合计约 7%，其中 pct20/pct60/mc 无影响）；
    /// 不含 dy_l：补它会让候选池缩小 37.5%（大量公司不分红）。
    /// </summary>
    private static readonly string[] AnalysisColumns =
        ["mc", "pettm", "pb", "roediluted", "npay", "oiy", "amount", "tr", "pct20", "pct60"];

    /// <summary>
    /// 补列用的宽区间（覆盖所有可能取值，含负值）
    /// </summary>
    private const string WideRange = "-99999999999999_99999999999999";

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

    public async Task<List<ScreenerAssetInfo>> ScreenAsync(object criteria, CancellationToken cancellationToken = default)
    {
        if (criteria is not StockCriteria stockCriteria)
        {
            throw new ArgumentException("筛选条件类型错误，期望 StockCriteria", nameof(criteria));
        }

        _logger.LogInformation("开始雪球选股，条件数量: {Count}, 候选池: {Limit}",
            stockCriteria.Criteria.Count, stockCriteria.Limit);

        try
        {
            await EnsureCookiesAsync(cancellationToken);

            var queryParams = BuildQueryParams(stockCriteria, DateOnly.FromDateTime(DateTime.Now));
            var stocks = await FetchFromXueqiuAsync(queryParams, cancellationToken);

            _logger.LogInformation("雪球选股完成，结果数量: {Count}", stocks.Count);
            return [.. stocks.Cast<ScreenerAssetInfo>()];
        }
        catch (Exception ex) when (ex is not ArgumentException and not OperationCanceledException)
        {
            _logger.LogError(ex, "雪球选股过程中发生错误");
            throw new FriendlyException($"筛选股票失败: {ex.Message}", ex);
        }
    }

    private async Task EnsureCookiesAsync(CancellationToken cancellationToken)
    {
        var cookies = _cookieContainer.GetCookies(new Uri("https://xueqiu.com"));
        if (cookies.Count > 0)
        {
            return;
        }

        _logger.LogDebug("雪球 Cookie 为空，访问首页获取 Cookie");
        using var client = _httpClientFactory.CreateClient("Xueqiu");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        using var response = await client.SendAsync(request, cancellationToken);

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
    /// 按指标口径选择财报报告期（YYYYMMDD 格式）。
    /// 累计类取最近完整年报：一季报的 ROE 只有全年的约 1/4，同一阈值在不同月份命中数相差十几倍
    /// （2026-11-28 实测 ROE≥10：一季报 73 家、年报 1092 家）；4 月 30 日披露截止，5 月起取上一年年报，1–4 月取前年年报。
    /// 增速类按披露截止日取最近一期已披露完的季报：5–8 月一季报，9–10 月半年报，11–12 月三季报，1–4 月上年三季报，
    /// 避免取到只披露了一部分公司的报告期。
    /// </summary>
    internal static string GetReportingPeriod(DateOnly today, bool isGrowthIndicator)
    {
        if (!isGrowthIndicator)
        {
            return today.Month >= 5 ? $"{today.Year - 1}1231" : $"{today.Year - 2}1231";
        }

        return today.Month switch
        {
            >= 11 => $"{today.Year}0930",
            >= 9 => $"{today.Year}0630",
            >= 5 => $"{today.Year}0331",
            _ => $"{today.Year - 1}0930"
        };
    }

    /// <summary>
    /// 把指标代码转成雪球查询键：财务指标拼接报告期后缀（field.YYYYMMDD），行情指标原样使用。
    /// </summary>
    private static string ResolveFieldKey(string code, DateOnly today)
    {
        if (CumulativeFields.Contains(code))
        {
            return $"{code}.{GetReportingPeriod(today, isGrowthIndicator: false)}";
        }

        return GrowthFields.Contains(code)
            ? $"{code}.{GetReportingPeriod(today, isGrowthIndicator: true)}"
            : code;
    }

    internal static string BuildQueryParams(StockCriteria criteria, DateOnly today)
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

        var filterParts = new List<string>();
        foreach (var condition in criteria.Criteria)
        {
            var min = condition.MinValue?.ToString(CultureInfo.InvariantCulture) ?? "";
            var max = condition.MaxValue?.ToString(CultureInfo.InvariantCulture) ?? "";
            filterParts.Add($"{ResolveFieldKey(condition.Code, today)}={min}_{max}");
        }

        // 排序键固定为规模与流动性优先（含成交额条件按成交额，否则按总市值），一律降序：
        // 条件只决定谁有资格入池。原先按"第一个条件 + 降序"取池，会让候选贴着条件的边缘或极端值
        // （2026-11-28 实测：价值股池 PE 全在 39.7–39.9，成长股增速中位数 369%/727%，高股息池最高 18.8% 的小盘）。
        var orderField = criteria.Criteria.Any(c => c.Code.Equals("amount", StringComparison.OrdinalIgnoreCase))
            ? "amount"
            : "mc";

        // 雪球响应只包含查询里出现的列：条件没涉及的关键指标用宽区间条件补出来，否则每只候选只有约 8 个字段，
        // 第 3 步看不到估值、盈利、成长、资金与趋势数据。
        foreach (var column in AnalysisColumns)
        {
            if (!criteria.Criteria.Any(c => c.Code.Equals(column, StringComparison.OrdinalIgnoreCase)))
            {
                filterParts.Add($"{ResolveFieldKey(column, today)}={WideRange}");
            }
        }

        var filterString = filterParts.Count > 0 ? "&" + string.Join("&", filterParts) : "";

        return $"category=CN&exchange={exchange}&indcode={indcode}" +
               $"&order_by={orderField}&order=desc&page=1&size={criteria.Limit}&only_count=0" +
               filterString;
    }

    private async Task<List<ScreenerStockInfo>> FetchFromXueqiuAsync(string queryParams, CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var url = $"{ScreenerEndpoint}?{queryParams}&_={timestamp}";

        _logger.LogDebug("调用雪球选股 API: {Url}", url);

        using var client = _httpClientFactory.CreateClient("Xueqiu");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await client.SendAsync(request, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);

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
