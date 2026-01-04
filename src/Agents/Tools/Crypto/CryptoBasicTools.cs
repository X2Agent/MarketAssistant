using System.Text.Json;
using System.Text.Json.Serialization;
using MarketAssistant.Agents.Plugins.Models;
using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.Tools.Crypto;

/// <summary>
/// 虚拟币基础数据工具实现
/// </summary>
public sealed class CryptoBasicTools : IBasicDataTools
{
    private readonly ILogger<CryptoBasicTools> _logger;
    private readonly HttpClient _httpClient;
    private readonly IUserSettingService _userSettingService;
    private const string BINANCE_API_BASE_URL = "https://api.binance.com/api/v3";

    public CryptoBasicTools(ILogger<CryptoBasicTools> logger, IUserSettingService userSettingService)
    {
        _logger = logger;
        _userSettingService = userSettingService;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    /// <summary>
    /// 根据资产代码获取基本数据（使用币安 API）
    /// </summary>
    public async Task<AssetQuoteInfo> GetAssetInfoAsync(string assetSymbol)
    {
        try
        {
            // 格式化交易对符号（如 "BTC" -> "BTCUSDT"）
            var symbol = FormatSymbol(assetSymbol);
            var url = $"{BINANCE_API_BASE_URL}/ticker/24hr?symbol={symbol}";

            _logger.LogInformation("正在获取虚拟币行情数据: {Symbol}", symbol);

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var tickerData = JsonSerializer.Deserialize<BinanceTicker24hr>(content);

            if (tickerData == null)
            {
                throw new InvalidOperationException($"无法解析币安API响应数据: {assetSymbol}");
            }

            // 映射到 AssetQuoteInfo 模型
            var quoteInfo = new AssetQuoteInfo
            {
                SecurityCode = assetSymbol.ToUpper(),
                SecurityName = assetSymbol.ToUpper(),
                SecurityType = "虚拟币",
                TradeStatus = "交易中",
                
                // 价格信息
                CurrentPrice = tickerData.LastPrice,
                OpenPrice = tickerData.OpenPrice,
                HighPrice = tickerData.HighPrice,
                LowPrice = tickerData.LowPrice,
                PreviousClosePrice = tickerData.PrevClosePrice,
                AveragePrice = tickerData.WeightedAvgPrice,
                
                // 涨跌信息
                PriceChange = tickerData.PriceChange,
                PercentageChange = tickerData.PriceChangePercent,
                Amplitude = CalculateAmplitude(tickerData.HighPrice, tickerData.LowPrice, tickerData.PrevClosePrice),
                
                // 交易量（币安单位：BTC 数量和 USDT 金额）
                Volume = tickerData.Volume / 10000m, // 转换为万手（这里作为万个币）
                Amount = tickerData.QuoteVolume / 100000000m, // 转换为亿 USDT
                
                // 虚拟币无以下字段，设为 0
                TurnoverRate = 0,
                PercentageChange3Day = 0,
                PercentageChange5Day = 0,
                TotalShares = 0,
                MarketCapitalization = 0,
                UpLimitPrice = 0,
                DownLimitPrice = 0,
                PERatio = 0,
                TTMPERatio = 0,
                PBRatio = 0,
                CirculationMarketCap = 0,
                NonRestrictedShares = 0,
                NetAssetPerShare = 0,
                VolumeRatio = 0,
                EntrustRatio = 0
            };

            _logger.LogInformation("成功获取虚拟币行情: {Symbol}, 当前价: {Price}, 涨跌幅: {Change}%", 
                assetSymbol, quoteInfo.CurrentPrice, quoteInfo.PercentageChange);

            return quoteInfo;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "调用币安API获取行情失败: {Symbol}", assetSymbol);
            throw new InvalidOperationException($"获取虚拟币行情失败: {assetSymbol}，请检查网络连接或交易对是否正确", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取虚拟币行情时发生错误: {Symbol}", assetSymbol);
            throw;
        }
    }

    /// <summary>
    /// 格式化交易对符号（如 "BTC" -> "BTCUSDT"）
    /// </summary>
    private string FormatSymbol(string symbol)
    {
        symbol = symbol.ToUpper().Trim();
        
        // 如果已经包含 USDT，直接返回
        if (symbol.EndsWith("USDT"))
        {
            return symbol;
        }
        
        // 否则添加 USDT 后缀
        return $"{symbol}USDT";
    }

    /// <summary>
    /// 计算振幅（%）
    /// </summary>
    private decimal CalculateAmplitude(decimal high, decimal low, decimal prevClose)
    {
        if (prevClose == 0) return 0;
        return ((high - low) / prevClose) * 100;
    }

    #region 币安 API 响应模型

    /// <summary>
    /// 币安 24 小时行情数据模型
    /// </summary>
    private class BinanceTicker24hr
    {
        public string Symbol { get; set; } = string.Empty;
        public decimal PriceChange { get; set; }
        public decimal PriceChangePercent { get; set; }
        public decimal WeightedAvgPrice { get; set; }
        public decimal PrevClosePrice { get; set; }
        public decimal LastPrice { get; set; }
        public decimal BidPrice { get; set; }
        public decimal AskPrice { get; set; }
        public decimal OpenPrice { get; set; }
        public decimal HighPrice { get; set; }
        public decimal LowPrice { get; set; }
        public decimal Volume { get; set; }
        public decimal QuoteVolume { get; set; }
        public long OpenTime { get; set; }
        public long CloseTime { get; set; }
        public int Count { get; set; }
    }

    #endregion

    /// <summary>
    /// 根据资产代码获取公司/项目基本面信息（使用 CoinGecko API）
    /// </summary>
    public async Task<CompanyInfo> GetCompanyInfoAsync(string assetSymbol)
    {
        try
        {
            // 将交易对符号映射到 CoinGecko ID
            var coinId = MapSymbolToCoinGeckoId(assetSymbol);
            var url = $"https://api.coingecko.com/api/v3/coins/{coinId}?localization=false&tickers=false&market_data=false&community_data=true&developer_data=true&sparkline=false";

            _logger.LogInformation("正在获取虚拟币项目信息: {Symbol} ({CoinId})", assetSymbol, coinId);

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var coinData = JsonSerializer.Deserialize<CoinGeckoResponse>(content);

            if (coinData == null)
            {
                throw new InvalidOperationException($"无法解析 CoinGecko API 响应数据: {assetSymbol}");
            }

            // 映射到 CompanyInfo 模型
            var companyInfo = new CompanyInfo
            {
                // 基础信息
                Name = coinData.Name ?? assetSymbol.ToUpper(),
                EName = coinData.Symbol?.ToUpper() ?? assetSymbol.ToUpper(),
                Market = "虚拟币市场",
                
                // 项目信息
                Description = CleanHtmlTags(coinData.Description?.En ?? "暂无描述"),
                Concept = string.Join(", ", coinData.Categories ?? new List<string>()),
                BusinessScope = coinData.Description?.En != null 
                    ? TruncateText(CleanHtmlTags(coinData.Description.En), 200) 
                    : "暂无经营范围描述",
                
                // 网站信息
                Website = coinData.Links?.Homepage?.FirstOrDefault(x => !string.IsNullOrEmpty(x)) ?? "",
                InfoWebsite = coinData.Links?.BlockchainSite?.FirstOrDefault(x => !string.IsNullOrEmpty(x)) ?? "",
                
                // 社区数据（映射到联系方式字段）
                CompanyPhone = coinData.CommunityData != null 
                    ? $"Twitter: {coinData.CommunityData.TwitterFollowers:N0} 关注者" 
                    : "",
                Email = coinData.CommunityData != null 
                    ? $"Reddit: {coinData.CommunityData.RedditSubscribers:N0} 订阅者" 
                    : "",
                
                // 开发者数据（映射到发行信息字段）
                Underwriter = coinData.Links?.ReposUrl?.GitHub?.FirstOrDefault() ?? "",
                RegisteredCapital = coinData.DeveloperData != null 
                    ? $"GitHub Stars: {coinData.DeveloperData.Stars:N0}" 
                    : "",
                
                // 成立日期
                EstablishmentDate = coinData.GenesisDate ?? "未知",
                ListingDate = coinData.GenesisDate ?? "未知",
                
                // 其他字段保持默认值（虚拟币不适用）
                InstitutionType = "加密货币项目",
                Organization = coinData.Categories?.FirstOrDefault() ?? "数字货币"
            };

            _logger.LogInformation("成功获取虚拟币项目信息: {Name} ({Symbol})", coinData.Name, assetSymbol);

            return companyInfo;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "调用 CoinGecko API 获取项目信息失败: {Symbol}", assetSymbol);
            throw new InvalidOperationException($"获取虚拟币项目信息失败: {assetSymbol}，请检查网络连接或币种代码是否正确", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取虚拟币项目信息时发生错误: {Symbol}", assetSymbol);
            throw;
        }
    }

    /// <summary>
    /// 将交易对符号映射到 CoinGecko ID
    /// </summary>
    private string MapSymbolToCoinGeckoId(string symbol)
    {
        symbol = symbol.ToLower().Replace("usdt", "").Trim();
        
        // 常见币种映射表
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "btc", "bitcoin" },
            { "eth", "ethereum" },
            { "bnb", "binancecoin" },
            { "xrp", "ripple" },
            { "ada", "cardano" },
            { "doge", "dogecoin" },
            { "sol", "solana" },
            { "dot", "polkadot" },
            { "matic", "matic-network" },
            { "avax", "avalanche-2" },
            { "link", "chainlink" },
            { "uni", "uniswap" },
            { "ltc", "litecoin" },
            { "etc", "ethereum-classic" },
            { "xlm", "stellar" },
            { "bch", "bitcoin-cash" },
            { "atom", "cosmos" },
            { "trx", "tron" },
            { "shib", "shiba-inu" },
            { "usdc", "usd-coin" },
            { "usdt", "tether" },
            { "dai", "dai" }
        };

        return mapping.TryGetValue(symbol, out var coinId) ? coinId : symbol;
    }

    /// <summary>
    /// 清理 HTML 标签
    /// </summary>
    private string CleanHtmlTags(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        
        // 简单的 HTML 标签清理（可考虑使用 HtmlAgilityPack 库获得更好效果）
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<.*?>", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
        return text.Trim();
    }

    /// <summary>
    /// 截断文本到指定长度
    /// </summary>
    private string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text;
        return text.Substring(0, maxLength) + "...";
    }

    #region CoinGecko API 响应模型

    /// <summary>
    /// CoinGecko 币种详情响应模型
    /// </summary>
    private class CoinGeckoResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("symbol")]
        public string Symbol { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public CoinDescription? Description { get; set; }

        [JsonPropertyName("links")]
        public CoinLinks? Links { get; set; }

        [JsonPropertyName("categories")]
        public List<string>? Categories { get; set; }

        [JsonPropertyName("genesis_date")]
        public string? GenesisDate { get; set; }

        [JsonPropertyName("community_data")]
        public CommunityData? CommunityData { get; set; }

        [JsonPropertyName("developer_data")]
        public DeveloperData? DeveloperData { get; set; }
    }

    private class CoinDescription
    {
        [JsonPropertyName("en")]
        public string? En { get; set; }
    }

    private class CoinLinks
    {
        [JsonPropertyName("homepage")]
        public List<string>? Homepage { get; set; }

        [JsonPropertyName("blockchain_site")]
        public List<string>? BlockchainSite { get; set; }

        [JsonPropertyName("repos_url")]
        public ReposUrl? ReposUrl { get; set; }
    }

    private class ReposUrl
    {
        [JsonPropertyName("github")]
        public List<string>? GitHub { get; set; }
    }

    private class CommunityData
    {
        [JsonPropertyName("twitter_followers")]
        public int TwitterFollowers { get; set; }

        [JsonPropertyName("reddit_subscribers")]
        public int RedditSubscribers { get; set; }
    }

    private class DeveloperData
    {
        [JsonPropertyName("stars")]
        public int Stars { get; set; }

        [JsonPropertyName("forks")]
        public int Forks { get; set; }

        public List<string> Repos
        {
            get
            {
                // 这里需要从 repos_url 中提取
                return new List<string>();
            }
        }
    }

    #endregion

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetAssetInfoAsync);
        yield return AIFunctionFactory.Create(GetCompanyInfoAsync);
    }
}





