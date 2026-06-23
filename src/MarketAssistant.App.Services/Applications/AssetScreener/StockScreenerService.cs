using System.Globalization;
using System.Net;
using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Infrastructure.Extensions;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.AssetScreener;

/// <summary>
/// 雪球网股票筛选服务（基于 HTTP API）
/// API 端点: GET https://xueqiu.com/service/screener/screen
/// 支持全部 38 个筛选指标（基本 15 + 行情 14 + 雪球社交 9）
/// </summary>
public sealed class StockScreenerService : IAssetScreenerService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CookieContainer _cookieContainer;
    private readonly ILogger<StockScreenerService> _logger;

    private const string ScreenerEndpoint = "/service/screener/screen";

    /// <summary>
    /// 需要拼接报告期日期后缀的财务指标（adj=1）
    /// 格式：field.YYYYMMDD=min_max
    /// </summary>
    private static readonly HashSet<string> Adj1Fields = new(StringComparer.OrdinalIgnoreCase)
    {
        "roediluted", "eps", "bps", "npay", "netprofit",
        "total_revenue", "oiy", "niota"
    };

    /// <summary>
    /// 行业枚举 → 雪球行业代码映射（与雪球网 screener API 的 indcode 参数一致）
    /// </summary>
    private static readonly Dictionary<IndustryType, string> IndustryCodeMap = new()
    {
        // 科技类
        { IndustryType.ComputerEquipment, "S7101" },
        { IndustryType.SoftwareDevelopment, "S7104" },
        { IndustryType.Semiconductor, "S2701" },
        // 新能源类
        { IndustryType.Battery, "S6307" },
        { IndustryType.PhotovoltaicEquipment, "S6305" },
        { IndustryType.WindPowerEquipment, "S6306" },
        // 医药类
        { IndustryType.ChemicalPharmaceutical, "S3701" },
        { IndustryType.BiologicalProducts, "S3703" },
        { IndustryType.MedicalDevices, "S3705" },
        // 消费类
        { IndustryType.Liquor, "S3405" },
        { IndustryType.BeveragesDairy, "S3407" },
        { IndustryType.FoodProcessing, "S3404" },
        // 金融类
        { IndustryType.JointStockBank, "S4803" },
        { IndustryType.StateBanks, "S4802" },
        // 房地产
        { IndustryType.RealEstateDevelopment, "S4301" },
        // 汽车类
        { IndustryType.PassengerVehicles, "S2805" },
        { IndustryType.AutoParts, "S2802" },
        // 通信类
        { IndustryType.CommunicationEquipment, "S7302" },
        { IndustryType.CommunicationServices, "S7301" },
        // 电力
        { IndustryType.Power, "S4101" },
        // 化工类
        { IndustryType.ChemicalMaterials, "S2202" },
        { IndustryType.ChemicalProducts, "S2203" },
        // 机械类
        { IndustryType.ConstructionMachinery, "S6406" },
        { IndustryType.SpecializedEquipment, "S6402" },
        // 家电类
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

    /// <summary>
    /// 根据筛选条件筛选股票
    /// </summary>
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
            // 确保 Cookie 已就绪（首次请求时访问雪球首页获取 cookiesu/device_id）
            await EnsureCookiesAsync();

            var (queryParams, orderField) = BuildQueryParams(stockCriteria);
            var stocks = await FetchFromXueqiuAsync(queryParams, orderField, stockCriteria.Limit);
            var limited = stocks.Take(stockCriteria.Limit).ToList();

            _logger.LogInformation("雪球选股完成，结果数量: {Count}", limited.Count);
            return limited.Cast<ScreenerAssetInfo>().ToList();
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            _logger.LogError(ex, "雪球选股过程中发生错误");
            throw new FriendlyException($"筛选股票失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 确保雪球 Cookie 已就绪。若 CookieContainer 中无雪球域名 Cookie，则先访问首页获取
    /// </summary>
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
        await client.SendAsync(request);
    }

    /// <summary>
    /// 获取最新可用财报报告期日期（雪球 adj=1 字段需要的 YYYYMMDD 格式）
    /// 规则：当前日期减 30 天，取最近一个季度末（0331/0630/0930/1231）
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
            _ => 12 // Q4 属于上一年
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
            12 => "1231",
            _ => "0331"
        };

        return $"{year}{day}";
    }

    /// <summary>
    /// 构建雪球 API 查询参数
    /// </summary>
    /// <returns>(查询参数字符串, 排序字段名)</returns>
    private (string queryParams, string orderField) BuildQueryParams(StockCriteria criteria)
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

        // 构建筛选条件参数（field=min_max 格式）
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

        var orderField = DetermineOrderField(criteria);
        var filterString = filterParts.Count > 0 ? "&" + string.Join("&", filterParts) : "";

        var queryParams = $"category=CN&exchange={exchange}&indcode={indcode}" +
                          $"&order_by={orderField}&order=desc&page=1&size={criteria.Limit}&only_count=0" +
                          filterString;

        return (queryParams, orderField);
    }

    /// <summary>
    /// 根据第一个有效筛选条件确定排序字段，默认按总市值排序
    /// </summary>
    private static string DetermineOrderField(StockCriteria criteria)
    {
        foreach (var condition in criteria.Criteria)
        {
            if (Adj1Fields.Contains(condition.Code) ||
                IsKnownMarketField(condition.Code))
            {
                return condition.Code;
            }
        }
        return "mc";
    }

    private static bool IsKnownMarketField(string code)
    {
        return code.ToLowerInvariant() is "pettm" or "pelyr" or "mc" or "fmc" or "pb" or "psr"
            or "pct" or "pct5" or "pct10" or "pct20" or "pct60" or "pct120" or "pct250"
            or "amount" or "volume" or "current" or "volume_ratio" or "tr" or "chgpct"
            or "pct_current_year" or "dy_l" or "eps" or "bps"
            or "follow" or "tweet" or "deal" or "follow7d" or "tweet7d" or "deal7d"
            or "follow7dpct" or "tweet7dpct" or "deal7dpct";
    }

    /// <summary>
    /// 调用雪球选股 API 获取股票列表
    /// </summary>
    private async Task<List<ScreenerStockInfo>> FetchFromXueqiuAsync(
        string queryParams, string orderField, int limit)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var url = $"{ScreenerEndpoint}?{queryParams}&_={timestamp}";

        _logger.LogDebug("调用雪球选股 API: {Url}", url);

        using var client = _httpClientFactory.CreateClient("Xueqiu");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return ParseResponse(json);
    }

    /// <summary>
    /// 解析雪球 API 响应 JSON
    /// </summary>
    private List<ScreenerStockInfo> ParseResponse(string json)
    {
        var stocks = new List<ScreenerStockInfo>();

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("list", out var list) ||
            list.ValueKind != JsonValueKind.Array)
        {
            _logger.LogWarning("雪球 API 返回数据格式异常");
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

        return stocks;
    }

    /// <summary>
    /// 解析单只股票数据，雪球 JSON 字段名与 ScreenerStockInfo 属性基本一致
    /// </summary>
    private ScreenerStockInfo? ParseStockItem(JsonElement item)
    {
        var name = GetString(item, "name");
        var symbol = GetString(item, "symbol");

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(symbol))
        {
            return null;
        }

        return new ScreenerStockInfo
        {
            Name = name,
            Symbol = symbol,
            // 基础字段（ScreenerAssetInfo）
            Current = GetDecimal(item, "current"),
            Pct = GetDecimal(item, "pct"),
            Amount = GetDecimal(item, "amount"),
            Mc = GetDecimal(item, "mc"),
            Fmc = GetDecimal(item, "fmc"),
            Volume = GetDecimal(item, "volume"),
            // 基本指标
            PeTtm = GetDecimal(item, "pettm"),
            PeLyr = GetDecimal(item, "pelyr"),
            Pb = GetDecimal(item, "pb"),
            Psr = GetDecimal(item, "psr"),
            RoeDiluted = GetDecimal(item, "roediluted"),
            Bps = GetDecimal(item, "bps"),
            Eps = GetDecimal(item, "eps"),
            NetProfit = GetDecimal(item, "netprofit"),
            TotalRevenue = GetDecimal(item, "total_revenue"),
            DyL = GetDecimal(item, "dy_l"),
            Npay = GetDecimal(item, "npay"),
            Oiy = GetDecimal(item, "oiy"),
            Niota = GetDecimal(item, "niota"),
            // 行情指标
            VolumeRatio = GetDecimal(item, "volume_ratio"),
            Tr = GetDecimal(item, "tr"),
            ChgPct = GetDecimal(item, "chgpct"),
            Pct5 = GetDecimal(item, "pct5"),
            Pct10 = GetDecimal(item, "pct10"),
            Pct20 = GetDecimal(item, "pct20"),
            Pct60 = GetDecimal(item, "pct60"),
            Pct120 = GetDecimal(item, "pct120"),
            Pct250 = GetDecimal(item, "pct250"),
            PctCurrentYear = GetDecimal(item, "pct_current_year"),
            // 雪球社交指标
            Follow = GetDecimal(item, "follow"),
            Tweet = GetDecimal(item, "tweet"),
            Deal = GetDecimal(item, "deal"),
            Follow7d = GetDecimal(item, "follow7d"),
            Tweet7d = GetDecimal(item, "tweet7d"),
            Deal7d = GetDecimal(item, "deal7d"),
            Follow7dPct = GetDecimal(item, "follow7dpct"),
            Tweet7dPct = GetDecimal(item, "tweet7dpct"),
            Deal7dPct = GetDecimal(item, "deal7dpct")
        };
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
