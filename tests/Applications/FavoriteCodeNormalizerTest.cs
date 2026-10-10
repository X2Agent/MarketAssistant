using MarketAssistant.Applications.Favorites;
using MarketAssistant.Infrastructure.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestMarketAssistant.Applications;

/// <summary>
/// <see cref="FavoriteCodeNormalizer"/> 归一化契约测试。
/// </summary>
/// <remarks>
/// 收藏状态判定完全依赖归一化口径一致（写入与查询走同一函数，SQLite 文本比较区分大小写）。
/// 交易所前缀剥离规则统一到 <see cref="MarketAssistant.Infrastructure.Core.StockSymbolConverter"/>
/// 后新增了 BJ——本测试锁定「沪深京一套规则 + 虚拟币原样」这一契约，
/// 防止某一侧回退成 StartsWith 简版导致 BJ 自选状态判失。
/// </remarks>
[TestClass]
public sealed class FavoriteCodeNormalizerTest
{
    [TestMethod]
    [TestCategory("Unit")]
    [DataRow("sh600519", "600519")]
    [DataRow("SH600519", "600519")]
    [DataRow("sz000001", "000001")]
    [DataRow("SZ000001", "000001")]
    [DataRow("bj430047", "430047")]
    [DataRow("BJ430047", "430047")]
    [DataRow("600519", "600519")]
    public void Normalize_AShare_ShouldUppercaseAndStripExchangePrefix(string input, string expected)
    {
        Assert.AreEqual(expected, FavoriteCodeNormalizer.Normalize(input, MarketType.AShare));
    }

    /// <summary>
    /// 虚拟币交易对保持原样（仅统一大写）：交易所前缀规则只属于 A 股，
    /// 把 "BTCUSDT" 之类按两字符前缀剥离会直接毁掉标的。
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    [DataRow("btcusdt", "BTCUSDT")]
    [DataRow("BTCUSDT", "BTCUSDT")]
    [DataRow("shibusdt", "SHIBUSDT")]
    public void Normalize_Crypto_ShouldOnlyUppercase(string input, string expected)
    {
        Assert.AreEqual(expected, FavoriteCodeNormalizer.Normalize(input, MarketType.Crypto));
    }

    [TestMethod]
    [TestCategory("Unit")]
    [DataRow("")]
    [DataRow("   ")]
    public void Normalize_OnBlank_ShouldReturnEmpty(string input)
    {
        Assert.AreEqual(string.Empty, FavoriteCodeNormalizer.Normalize(input, MarketType.AShare));
        Assert.AreEqual(string.Empty, FavoriteCodeNormalizer.Normalize(input, MarketType.Crypto));
    }
}
