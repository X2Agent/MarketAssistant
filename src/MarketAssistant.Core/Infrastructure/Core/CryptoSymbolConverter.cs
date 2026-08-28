namespace MarketAssistant.Infrastructure.Core;

/// <summary>
/// 虚拟币交易对符号格式转换工具类
/// </summary>
public static class CryptoSymbolConverter
{
    /// <summary>
    /// 包装币白名单：其代码本身以 BTC/ETH/BNB 等计价货币字母结尾，
    /// 但并非交易对，须短路处理，避免被误判为“已含计价货币”。
    /// </summary>
    private static readonly HashSet<string> WrappedBaseCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "WBTC", "WETH", "STETH", "WBNB"
    };

    /// <summary>
    /// 将币种代码转换为币安交易对格式（如 BTC → BTCUSDT）
    /// </summary>
    /// <param name="symbol">币种代码，支持格式：BTC、BTCUSDT、BTC/USDT、BTC-USDT</param>
    /// <param name="quoteCurrency">计价货币，默认为 USDT</param>
    /// <returns>币安格式交易对（如 BTCUSDT）</returns>
    public static string ToBinanceFormat(string symbol, string quoteCurrency = "USDT")
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("币种代码不能为空", nameof(symbol));
        }

        symbol = symbol.Replace("crypto.", "", StringComparison.OrdinalIgnoreCase)
                       .Replace("/", "")
                       .Replace("-", "")
                       .Replace(" ", "")
                       .ToUpperInvariant();

        quoteCurrency = quoteCurrency.ToUpperInvariant();

        // 包装币（WBTC/WETH 等）本身以计价货币字母结尾，须先于后缀判断短路，避免漏加计价货币
        if (WrappedBaseCurrencies.Contains(symbol))
        {
            return $"{symbol}{quoteCurrency}";
        }

        // 如果已经包含计价货币后缀，直接返回
        var quoteCurrencies = new[] { "USDT", "BUSD", "USDC", "BTC", "ETH", "BNB", "DAI" };
        foreach (var quote in quoteCurrencies)
        {
            if (symbol.EndsWith(quote) && symbol.Length > quote.Length)
            {
                return symbol;
            }
        }

        // 添加计价货币
        return $"{symbol}{quoteCurrency}";
    }

    /// <summary>
    /// 提取基础币种名称（移除计价货币后缀）
    /// </summary>
    /// <param name="tradingPair">交易对，如 BTCUSDT</param>
    /// <returns>基础币种，如 BTC</returns>
    public static string ExtractBaseCurrency(string tradingPair)
    {
        if (string.IsNullOrWhiteSpace(tradingPair))
        {
            return string.Empty;
        }

        tradingPair = tradingPair.ToUpperInvariant();

        // 包装币（WBTC/WETH 等）本身以计价货币字母结尾，须先于后缀剥离短路，避免被截断成错误基础币
        if (WrappedBaseCurrencies.Contains(tradingPair))
        {
            return tradingPair;
        }

        // 移除常见计价货币后缀（按长度倒序，避免误匹配）
        var quoteCurrencies = new[] { "USDT", "BUSD", "USDC", "DAI", "BTC", "ETH", "BNB" };
        foreach (var quote in quoteCurrencies)
        {
            if (tradingPair.EndsWith(quote) && tradingPair.Length > quote.Length)
            {
                return tradingPair.Substring(0, tradingPair.Length - quote.Length);
            }
        }

        // 如果没有匹配的后缀，返回原字符串（可能本身就是基础币种）
        return tradingPair;
    }

    /// <summary>
    /// 格式化为CoinGecko ID（用于调用CoinGecko API）
    /// </summary>
    /// <param name="symbol">币种代码，如 BTC、BTCUSDT</param>
    /// <returns>CoinGecko ID，如 bitcoin</returns>
    public static string ToCoinGeckoId(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return string.Empty;
        }

        // 标准化输入（移除前缀、转大写）
        symbol = symbol.Replace("crypto.", "", StringComparison.OrdinalIgnoreCase)
                       .Replace("/", "")
                       .Replace("-", "")
                       .Replace(" ", "")
                       .ToUpperInvariant();

        // 提取基础币种
        var baseCurrency = ExtractBaseCurrency(symbol);

        // 如果提取失败（说明没有已知后缀），使用原始symbol
        if (string.IsNullOrEmpty(baseCurrency))
        {
            baseCurrency = symbol;
        }

        baseCurrency = baseCurrency.ToLower();

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
            { "dai", "dai" },
            { "busd", "binance-usd" }
        };

        return mapping.TryGetValue(baseCurrency, out var coinId) ? coinId : baseCurrency;
    }
}
