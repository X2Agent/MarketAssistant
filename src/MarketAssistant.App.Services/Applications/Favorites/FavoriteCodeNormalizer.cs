namespace MarketAssistant.Applications.Favorites;

/// <summary>
/// 收藏（自选）代码归一化工具：所有写入/查询路径共用唯一入口，
/// 保证 SQLite 文本比较（区分大小写）下收藏状态判定一致。
/// 规则：统一大写；A股剥离 SH/SZ 交易所前缀；虚拟币保持交易对原样（大写）。
/// </summary>
public static class FavoriteCodeNormalizer
{
    /// <summary>
    /// 按市场归一化收藏代码（如 sh600519 → 600519、btcusdt → BTCUSDT）。
    /// </summary>
    /// <param name="code">原始资产代码（可能带交易所前缀或大小写混杂）</param>
    /// <param name="marketType">资产所属市场</param>
    /// <returns>归一化后的代码；输入为空白时返回空字符串</returns>
    public static string Normalize(string code, MarketType marketType)
    {
        if (string.IsNullOrWhiteSpace(code))
            return string.Empty;

        var normalized = code.Trim().ToUpperInvariant();

        if (marketType == MarketType.AShare
            && normalized.Length > 2
            && (normalized.StartsWith("SH", StringComparison.Ordinal)
                || normalized.StartsWith("SZ", StringComparison.Ordinal)))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }
}