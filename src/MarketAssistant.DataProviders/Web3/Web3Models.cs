using System.Text.Json.Serialization;

namespace MarketAssistant.DataProviders.Web3;

/// <summary>
/// DexScreener / GoPlus 响应模型（仅服务数据提供者内部反序列化使用）。
/// </summary>

public class DexScreenerSearchResponse
{
    [JsonPropertyName("pairs")]
    public List<DexScreenerPair> Pairs { get; set; } = [];
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

/// <summary>
/// GoPlus 响应外壳（code 1 = 成功），result 以代币地址为键；EVM 与 Solana 端点仅 result 值类型不同。
/// </summary>
public class GoPlusResponse<TSecurity>
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("result")]
    public Dictionary<string, TSecurity>? Result { get; set; }
}

/// <summary>
/// GoPlus EVM 主端点代币安全字段（字段名对照官方 Response Details）。
/// 风险标志位为字符串 "1"/"0"；字段缺失表示未知（闭源合约的多数风险项不返回），不等于安全。
/// </summary>
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

    /// <summary>所有者地址：空串表示合约无所有者，字段缺失表示未知。</summary>
    [JsonPropertyName("owner_address")]
    public string? OwnerAddress { get; set; }

    [JsonPropertyName("can_take_back_ownership")]
    public string? CanTakeBackOwnership { get; set; }

    [JsonPropertyName("owner_change_balance")]
    public string? OwnerChangeBalance { get; set; }

    [JsonPropertyName("hidden_owner")]
    public string? HiddenOwner { get; set; }

    [JsonPropertyName("selfdestruct")]
    public string? Selfdestruct { get; set; }

    [JsonPropertyName("is_blacklisted")]
    public string? IsBlacklisted { get; set; }

    [JsonPropertyName("is_whitelisted")]
    public string? IsWhitelisted { get; set; }

    [JsonPropertyName("anti_whale_modifiable")]
    public string? AntiWhaleModifiable { get; set; }

    [JsonPropertyName("trading_cooldown")]
    public string? TradingCooldown { get; set; }

    [JsonPropertyName("slippage_modifiable")]
    public string? SlippageModifiable { get; set; }

    [JsonPropertyName("personal_slippage_modifiable")]
    public string? PersonalSlippageModifiable { get; set; }

    [JsonPropertyName("transfer_pausable")]
    public string? TransferPausable { get; set; }

    /// <summary>沙箱买入失败为 "1"；反机器人机制也可能触发，此时 buy_tax 同样返回 "1"。</summary>
    [JsonPropertyName("cannot_buy")]
    public string? CannotBuy { get; set; }

    [JsonPropertyName("cannot_sell_all")]
    public string? CannotSellAll { get; set; }

    [JsonPropertyName("is_honeypot")]
    public string? IsHoneypot { get; set; }

    [JsonPropertyName("honeypot_with_same_creator")]
    public string? HoneypotWithSameCreator { get; set; }

    [JsonPropertyName("is_airdrop_scam")]
    public string? IsAirdropScam { get; set; }

    [JsonPropertyName("buy_tax")]
    public string? BuyTax { get; set; }

    [JsonPropertyName("sell_tax")]
    public string? SellTax { get; set; }

    [JsonPropertyName("holder_count")]
    public string? HolderCount { get; set; }

    /// <summary>所有者持有代币占总量的比例（1 = 100%）；所有者地址为空或未知时不返回。</summary>
    [JsonPropertyName("owner_percent")]
    public string? OwnerPercent { get; set; }

    /// <summary>创建者持有代币占总量的比例（1 = 100%）。</summary>
    [JsonPropertyName("creator_percent")]
    public string? CreatorPercent { get; set; }

    /// <summary>"1" 表示 GoPlus 认定的知名可信代币；缺失不代表有风险。</summary>
    [JsonPropertyName("trust_list")]
    public string? TrustList { get; set; }

    [JsonPropertyName("is_in_cex")]
    public GoPlusCexListing? IsInCex { get; set; }

    /// <summary>仅在检出仿冒主流代币时返回。</summary>
    [JsonPropertyName("fake_token")]
    public GoPlusFakeToken? FakeToken { get; set; }

    [JsonPropertyName("other_potential_risks")]
    public string? OtherPotentialRisks { get; set; }
}

/// <summary>主流中心化交易所上线标记：listed 为 "1" 表示已上线，官方称此时其他风险提示通常可忽略。</summary>
public class GoPlusCexListing
{
    [JsonPropertyName("listed")]
    public string? Listed { get; set; }
}

/// <summary>仿冒主流代币标记：value 为 1 表示该代币仿冒了 true_token_address 所列的真实主流代币（多个以逗号分隔）。</summary>
public class GoPlusFakeToken
{
    [JsonPropertyName("true_token_address")]
    public string? TrueTokenAddress { get; set; }

    [JsonPropertyName("value")]
    public int? Value { get; set; }
}

/// <summary>
/// GoPlus Solana 专用端点代币安全字段。数值型标志位在响应中有的是数字、有的是字符串，
/// 统一以 int? 承接（System.Text.Json Web 默认允许从字符串读取数字）。
/// </summary>
public class GoPlusSolanaTokenSecurity
{
    [JsonPropertyName("metadata")]
    public GoPlusSolanaMetadata? Metadata { get; set; }

    /// <summary>1 表示 GoPlus 认定的知名可信代币。</summary>
    [JsonPropertyName("trusted_token")]
    public int? TrustedToken { get; set; }

    [JsonPropertyName("holder_count")]
    public string? HolderCount { get; set; }

    /// <summary>1 表示不可转让（灵魂绑定）代币。</summary>
    [JsonPropertyName("non_transferable")]
    public int? NonTransferable { get; set; }

    /// <summary>新建代币账户的默认状态：1 = 正常，2 = 默认冻结。</summary>
    [JsonPropertyName("default_account_state")]
    public int? DefaultAccountState { get; set; }

    [JsonPropertyName("mintable")]
    public GoPlusSolanaAuthority? Mintable { get; set; }

    [JsonPropertyName("freezable")]
    public GoPlusSolanaAuthority? Freezable { get; set; }

    [JsonPropertyName("closable")]
    public GoPlusSolanaAuthority? Closable { get; set; }

    [JsonPropertyName("balance_mutable_authority")]
    public GoPlusSolanaAuthority? BalanceMutableAuthority { get; set; }

    [JsonPropertyName("transfer_fee_upgradable")]
    public GoPlusSolanaAuthority? TransferFeeUpgradable { get; set; }

    /// <summary>启用的转账钩子程序列表，空数组表示未启用。</summary>
    [JsonPropertyName("transfer_hook")]
    public List<JsonElement>? TransferHook { get; set; }
}

public class GoPlusSolanaMetadata
{
    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }
}

/// <summary>Solana 权限项：status 为 1 表示权限仍存在（未被放弃），authority 为持有该权限的地址。</summary>
public class GoPlusSolanaAuthority
{
    [JsonPropertyName("status")]
    public int? Status { get; set; }

    [JsonPropertyName("authority")]
    public List<GoPlusSolanaAuthorityAddress>? Authority { get; set; }
}

public class GoPlusSolanaAuthorityAddress
{
    /// <summary>1 表示该地址被 GoPlus 标记为恶意地址。</summary>
    [JsonPropertyName("malicious_address")]
    public int? MaliciousAddress { get; set; }
}
