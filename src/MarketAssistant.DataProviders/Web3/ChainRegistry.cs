namespace MarketAssistant.DataProviders.Web3;

/// <summary>
/// Web3 链 ID 统一注册表：将多种链 ID 写法归一化为规范链名，
/// 并转换各数据源所需词汇（DexScreener 要求链全名，GoPlus 主端点要求数字链 ID）。
/// </summary>
public static class ChainRegistry
{
    /// <summary>规范链信息：GoPlusChainId 为 GoPlus 主端点的数字链 ID，null 表示须走 Solana 专用端点。</summary>
    private static readonly Dictionary<string, string?> Chains = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ethereum"] = "1",
        ["bsc"] = "56",
        ["polygon"] = "137",
        ["arbitrum"] = "42161",
        ["optimism"] = "10",
        ["avalanche"] = "43114",
        ["base"] = "8453",
        ["solana"] = null
    };

    /// <summary>别名 → 规范链名（含规范名自身、CoinGecko 平台 ID、EIP-155 数字链 ID、常用缩写）。</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ethereum"] = "ethereum", ["eth"] = "ethereum", ["eth-mainnet"] = "ethereum", ["1"] = "ethereum",
        ["bsc"] = "bsc", ["binance-smart-chain"] = "bsc", ["binance_smart_chain"] = "bsc", ["bnb"] = "bsc", ["56"] = "bsc",
        ["polygon"] = "polygon", ["polygon-pos"] = "polygon", ["matic"] = "polygon", ["matic-network"] = "polygon", ["137"] = "polygon",
        ["arbitrum"] = "arbitrum", ["arbitrum-one"] = "arbitrum", ["arb"] = "arbitrum", ["42161"] = "arbitrum",
        ["optimism"] = "optimism", ["optimistic-ethereum"] = "optimism", ["10"] = "optimism",
        ["avalanche"] = "avalanche", ["avax"] = "avalanche", ["avalanche-2"] = "avalanche", ["43114"] = "avalanche",
        ["base"] = "base", ["8453"] = "base",
        ["solana"] = "solana", ["sol"] = "solana", ["mainnet"] = "solana"
    };

    /// <summary>
    /// 归一化为规范链名（ethereum/bsc/polygon/arbitrum/optimism/avalanche/base/solana）。
    /// 未识别的输入小写后原样透传，保证 DexScreener 的长尾链仍可用。
    /// </summary>
    public static string Normalize(string? chainId)
    {
        if (string.IsNullOrWhiteSpace(chainId))
            throw new FriendlyException("缺少链 ID，支持的主要链：" + string.Join("、", Chains.Keys));

        var key = chainId.Trim();
        return Aliases.TryGetValue(key, out var canonical) ? canonical : key.ToLowerInvariant();
    }

    /// <summary>
    /// 规范链名 → GoPlus 数字链 ID；Solana 返回 null（须走 GoPlus 专用 Solana 端点）。
    /// 未收录的链返回 null，由调用方决定透传或报错。
    /// </summary>
    public static string? ToGoPlusChainId(string canonical) =>
        Chains.TryGetValue(canonical, out var goPlusChainId) ? goPlusChainId : null;
}