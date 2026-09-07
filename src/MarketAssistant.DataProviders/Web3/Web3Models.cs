using System.Text.Json.Serialization;

namespace MarketAssistant.DataProviders.Web3;

/// <summary>
/// DexScreener / GoPlus 响应模型（仅服务数据提供者内部反序列化使用）。
/// </summary>

public class DexScreenerSearchResponse
{
    [JsonPropertyName("pairs")]
    public List<DexScreenerPair> Pairs { get; set; } = new();
}

public class DexScreenerPair
{
    [JsonPropertyName("chainId")]
    public string ChainId { get; set; } = string.Empty;

    [JsonPropertyName("dexId")]
    public string DexId { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("baseToken")]
    public DexScreenerToken BaseToken { get; set; } = new();

    [JsonPropertyName("quoteToken")]
    public DexScreenerToken QuoteToken { get; set; } = new();

    [JsonPropertyName("priceUsd")]
    public string? PriceUsd { get; set; }

    [JsonPropertyName("volume")]
    public DexScreenerVolume? Volume { get; set; }

    [JsonPropertyName("priceChange")]
    public DexScreenerPriceChange? PriceChange { get; set; }

    [JsonPropertyName("liquidity")]
    public DexScreenerLiquidity? Liquidity { get; set; }

    [JsonPropertyName("fdv")]
    public decimal? Fdv { get; set; }

    [JsonPropertyName("marketCap")]
    public decimal? MarketCap { get; set; }

    [JsonPropertyName("pairCreatedAt")]
    public long? PairCreatedAt { get; set; }
}

public class DexScreenerToken
{
    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;
}

public class DexScreenerVolume
{
    [JsonPropertyName("h24")]
    public decimal? H24 { get; set; }

    [JsonPropertyName("h6")]
    public decimal? H6 { get; set; }

    [JsonPropertyName("h1")]
    public decimal? H1 { get; set; }
}

public class DexScreenerPriceChange
{
    [JsonPropertyName("m5")]
    public decimal? M5 { get; set; }

    [JsonPropertyName("h1")]
    public decimal? H1 { get; set; }

    [JsonPropertyName("h6")]
    public decimal? H6 { get; set; }

    [JsonPropertyName("h24")]
    public decimal? H24 { get; set; }
}

public class DexScreenerLiquidity
{
    [JsonPropertyName("usd")]
    public decimal? Usd { get; set; }

    [JsonPropertyName("base")]
    public decimal? Base { get; set; }

    [JsonPropertyName("quote")]
    public decimal? Quote { get; set; }
}

public class GoPlusSecurityResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("result")]
    public Dictionary<string, GoPlusTokenSecurity>? Result { get; set; }
}

public class GoPlusTokenSecurity
{
    [JsonPropertyName("token_symbol")]
    public string? TokenSymbol { get; set; }

    [JsonPropertyName("token_name")]
    public string? TokenName { get; set; }

    [JsonPropertyName("is_open_source")]
    public string? IsOpenSource { get; set; }

    [JsonPropertyName("is_proxy")]
    public string? IsProxy { get; set; }

    [JsonPropertyName("is_mintable")]
    public string? IsMintable { get; set; }

    [JsonPropertyName("owner_percentage")]
    public string? OwnerPercentage { get; set; }

    [JsonPropertyName("creator_percentage")]
    public string? CreatorPercentage { get; set; }

    [JsonPropertyName("holder_count")]
    public string? HolderCount { get; set; }

    [JsonPropertyName("is_in_dex")]
    public string? IsInDex { get; set; }

    [JsonPropertyName("is_anti_whale")]
    public string? IsAntiWhale { get; set; }

    [JsonPropertyName("is_honeypot")]
    public string? IsHoneypot { get; set; }

    [JsonPropertyName("is_black_list")]
    public string? IsBlackList { get; set; }

    [JsonPropertyName("is_whitelist")]
    public string? IsWhiteList { get; set; }

    [JsonPropertyName("is_owner_can_change_balance")]
    public string? IsOwnerCanChangeBalance { get; set; }

    [JsonPropertyName("cannot_sell_all")]
    public string? CannotSellAll { get; set; }

    [JsonPropertyName("slippage_modifiable")]
    public string? SlippageModifiable { get; set; }

    [JsonPropertyName("is_slippage_modified")]
    public string? IsSlippageModified { get; set; }

    [JsonPropertyName("trading_cooldown")]
    public string? TradingCooldown { get; set; }

    [JsonPropertyName("personal_slippage_modifiable")]
    public string? PersonalSlippageModifiable { get; set; }

    [JsonPropertyName("hidden_owner")]
    public string? HiddenOwner { get; set; }

    [JsonPropertyName("anti_whale_modifiable")]
    public string? AntiWhaleModifiable { get; set; }

    [JsonPropertyName("cannot_be_modified")]
    public string? CannotBeModified { get; set; }

    [JsonPropertyName("is_owner_limit")]
    public string? IsOwnerLimit { get; set; }

    [JsonPropertyName("buy_tax")]
    public string? BuyTax { get; set; }

    [JsonPropertyName("sell_tax")]
    public string? SellTax { get; set; }

    [JsonPropertyName("is_true_token")]
    public string? IsTrueToken { get; set; }

    [JsonPropertyName("is_airdrop_scam")]
    public string? IsAirdropScam { get; set; }

    [JsonPropertyName("is_1000_token")]
    public string? Is1000Token { get; set; }

    [JsonPropertyName("honeypot_with_same_owner")]
    public string? HoneypotWithSameOwner { get; set; }

    [JsonPropertyName("transfer_pausable")]
    public string? TransferPausable { get; set; }

    [JsonPropertyName("external_call")]
    public string? ExternalCall { get; set; }

    [JsonPropertyName("owner_change_balance")]
    public string? OwnerChangeBalance { get; set; }

    [JsonPropertyName("self_destruct")]
    public string? SelfDestruct { get; set; }

    [JsonPropertyName("trust_list")]
    public List<GoPlusDexInfo>? TrustList { get; set; }

    [JsonPropertyName("dex")]
    public List<GoPlusDexInfo>? Dex { get; set; }

    [JsonPropertyName("other_potential_risks")]
    public string? OtherPotentialRisks { get; set; }

    [JsonPropertyName("unverifyed")]
    public string? Unverifyed { get; set; }

    [JsonPropertyName("fake_token")]
    public string? FakeToken { get; set; }
}

public class GoPlusDexInfo
{
    [JsonPropertyName("liquidity")]
    public string? Liquidity { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("quote_address")]
    public string? QuoteAddress { get; set; }

    [JsonPropertyName("pair_address")]
    public string? PairAddress { get; set; }
}