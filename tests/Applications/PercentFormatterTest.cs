using MarketAssistant.Applications.Assets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Globalization;

namespace TestMarketAssistant.Applications;

/// <summary>
/// <see cref="PercentFormatter"/> 格式化契约测试。
/// </summary>
/// <remarks>
/// 锁定三件事：① 显式正负号（涨跌配色的上游依据，丢了正号等于丢失颜色语义）；
/// ② 零值无符号；③ 与区域无关（下游 PriceChangeColorConverter 以 InvariantCulture
/// 解析资产列表的涨跌幅字符串，格式侧必须同口径，否则非拉丁数字区域下解析失败变灰）。
/// </remarks>
[TestClass]
public sealed class PercentFormatterTest
{
    [TestMethod]
    [TestCategory("Unit")]
    [DataRow(1.23d, "+1.23%")]
    [DataRow(-0.82d, "-0.82%")]
    [DataRow(0d, "0.00%")]
    [DataRow(0.004d, "0.00%")]
    public void Format_ShouldProduceSignedPercentWithTwoDecimals(double percent, string expected)
    {
        Assert.AreEqual(expected, PercentFormatter.Format(percent));
    }

    [TestMethod]
    [TestCategory("Unit")]
    [DataRow(1.23d, "+1.23")]
    [DataRow(-0.82d, "-0.82")]
    [DataRow(0d, "0.00")]
    public void FormatValue_ShouldProduceSignedValueWithoutPercentSign(double value, string expected)
    {
        Assert.AreEqual(expected, PercentFormatter.FormatValue((decimal)value));
    }

    /// <summary>
    /// 自适应精度：去掉多余尾零（告警规则浮动值按用户输入位数展示）。
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    [DataRow(5d, "+5")]
    [DataRow(-1.25d, "-1.25")]
    [DataRow(0d, "0")]
    [DataRow(3.100d, "+3.1")]
    public void FormatCompact_ShouldDropTrailingZeros(double value, string expected)
    {
        Assert.AreEqual(expected, PercentFormatter.FormatCompact((decimal)value));
    }

    /// <summary>
    /// decimal 与 double 两个重载必须给出一致结果——上游数据源两种类型都有
    /// （财联社比率为 decimal、新浪资金流比率为 double），展示口径不能分流。
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void Format_DecimalAndDoubleOverloads_ShouldAgree()
    {
        Assert.AreEqual(
            PercentFormatter.Format(-0.82d),
            PercentFormatter.Format(-0.82m));
    }

    /// <summary>
    /// 区域独立性：佛历等区域会改写年份/数字形态，涨跌幅是协议与配色依据，必须钉不变量。
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    [DataRow("th-TH")]
    [DataRow("de-DE")]
    [DataRow("en-US")]
    public void Format_UnderAnyCulture_ShouldBeInvariant(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            Assert.AreEqual("-0.82%", PercentFormatter.Format(-0.82d), $"[{culture}]");
            Assert.AreEqual("+1.23%", PercentFormatter.Format(1.23d), $"[{culture}]");
            Assert.AreEqual("+5", PercentFormatter.FormatCompact(5m), $"[{culture}]");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
