using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Applications.Cache;

/// <summary>
/// Preferences 键定义中心：所有 Preferences 持久化键在此统一管理，避免硬编码字符串散落在各处。
/// 市场相关键通过 <see cref="MarketType"/> 枚举名动态拼接后缀，新增市场无需修改本类。
/// 与市场无关的全局键以常量形式提供。
/// </summary>
public static class PreferenceKeys
{
    /// <summary>
    /// 用户设置持久化键。
    /// </summary>
    public const string UserSettings = "UserSettings";

    /// <summary>
    /// 价格提醒规则持久化键（与市场无关，全局存储）。
    /// </summary>
    public const string PriceAlertRules = "PriceAlertRules";

    /// <summary>
    /// 交易风险配置持久化键（与市场无关，全局存储）。
    /// </summary>
    public const string TradingRiskConfig = "TradingRiskConfig";

    /// <summary>
    /// 根据市场类型获取收藏资产持久化键。
    /// </summary>
    public static string GetFavoriteAssetsKey(MarketType marketType) => $"FavoriteAssets_{marketType}";

    /// <summary>
    /// 根据市场类型获取最近浏览资产持久化键。
    /// </summary>
    public static string GetRecentAssetsKey(MarketType marketType) => $"RecentAssets_{marketType}";

    /// <summary>
    /// 根据市场类型获取交易风险配置持久化键。
    /// </summary>
    public static string GetTradingRiskConfigKey(MarketType marketType) => $"TradingRiskConfig_{marketType}";

    /// <summary>
    /// 根据市场类型获取价格提醒规则持久化键。
    /// </summary>
    public static string GetPriceAlertRulesKey(MarketType marketType) => $"PriceAlertRules_{marketType}";
}
