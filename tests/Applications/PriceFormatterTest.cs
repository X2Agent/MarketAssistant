using MarketAssistant.Applications.Assets;
using MarketAssistant.Infrastructure.Core;
using System.Globalization;

namespace TestMarketAssistant.Applications;

/// <summary>
/// <see cref="PriceFormatter"/> 取位规则契约测试。
/// </summary>
/// <remarks>
/// 锁定两件事：① A 股按最小报价单位取位（股票 0.01 → 2 位，基金/ETF 0.001 → 3 位），
/// 不沿用虚拟币的量级分档，否则 21.30 会显示成 21.3000、涨跌额 0.47 会显示成 0.470000；
/// ② 虚拟币仍按量级分档（低价币需要更多小数位）。
/// 数字分组符随区域变化，测试固定为 Invariant，避免在不同区域的开发机上漂移。
/// </remarks>
[TestClass]
public sealed class PriceFormatterTest
{
    private CultureInfo _originalCulture = CultureInfo.CurrentCulture;

    [TestInitialize]
    public void UseInvariantCulture()
    {
        _originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [TestCleanup]
    public void RestoreCulture()
    {
        CultureInfo.CurrentCulture = _originalCulture;
    }

    [TestMethod]
    [TestCategory("Unit")]
    [DataRow(21.3d, "600519", "21.30")]
    [DataRow(1800.5d, "SH600519", "1,800.50")]
    [DataRow(0.47d, "000001", "0.47")]
    [DataRow(10d, null, "10.00")]
    public void Format_AShareStock_ShouldUseTwoDecimals(double price, string? code, string expected)
    {
        Assert.AreEqual(expected, PriceFormatter.Format((decimal)price, MarketType.AShare, code));
    }

    [TestMethod]
    [TestCategory("Unit")]
    [DataRow(141.339d, "511010", "141.339")]
    [DataRow(4.1d, "159915", "4.100")]
    [DataRow(0.001d, "SZ161725", "0.001")]
    public void Format_AShareFundOrEtf_ShouldUseThreeDecimals(double price, string code, string expected)
    {
        Assert.AreEqual(expected, PriceFormatter.Format((decimal)price, MarketType.AShare, code));
    }

    [TestMethod]
    [TestCategory("Unit")]
    [DataRow(67234.5d, "67,234.50")]
    [DataRow(1000d, "1,000.00")]
    [DataRow(999.99d, "999.9900")]
    [DataRow(1d, "1.0000")]
    [DataRow(0.5d, "0.500000")]
    [DataRow(0.00001234d, "0.000012")]
    public void Format_Crypto_ShouldUseMagnitudeTiers(double price, string expected)
    {
        Assert.AreEqual(expected, PriceFormatter.Format((decimal)price, MarketType.Crypto));
    }
}
