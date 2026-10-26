using MarketAssistant.Infrastructure.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Globalization;

namespace TestMarketAssistant.Infrastructure;

/// <summary>
/// <see cref="DateKeys"/> 日期键契约测试。
/// </summary>
/// <remarks>
/// 日期键是持久化分组键（日统计/账户快照/简报当天缓存/图谱生效日），
/// 两个不变量必须锁定：① 定长零填充 yyyy-MM-dd（字典序即时序的比较前提）；
/// ② 与区域无关（佛历区域下 DateTime 年份会变成 2569，破坏一切日期比较）。
/// </remarks>
[TestClass]
public sealed class DateKeysTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public void Today_ShouldMatchLocalNowWithFixedShape()
    {
        var expected = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        Assert.AreEqual(expected, DateKeys.Today());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void From_ShouldFormatGivenDate()
    {
        Assert.AreEqual("2026-10-09", DateKeys.From(new DateTime(2026, 10, 9, 23, 59, 0)));
        Assert.AreEqual("2026-01-01", DateKeys.From(new DateTime(2026, 1, 1)));
    }

    /// <summary>
    /// 区域独立性回归防护：th-TH 佛历区域下默认格式会产出佛历年份，
    /// 而日期键写入 SQLite 后参与字典序比较与相等匹配，必须钉 InvariantCulture。
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    [DataRow("th-TH")]
    [DataRow("ar-SA")]
    [DataRow("en-US")]
    [DataRow("zh-CN")]
    public void Today_UnderAnyCulture_ShouldUseGregorianInvariantKeys(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            var key = DateKeys.Today();
            StringAssert.StartsWith(key, DateTime.Now.Year.ToString(CultureInfo.InvariantCulture), $"[{culture}] 年份应为公历");
            Assert.IsTrue(
                System.Text.RegularExpressions.Regex.IsMatch(key, @"^\d{4}-\d{2}-\d{2}$"),
                $"[{culture}] 日期键形状应为 yyyy-MM-dd，实际为: {key}");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
