using MarketAssistant.Infrastructure.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Globalization;

namespace Tests;

// CultureInfo.CurrentCulture 为线程级状态，用例会临时切换区域，
// 必须禁止并行执行以免污染其它测试
[DoNotParallelize]
[TestClass]
public class StockSymbolConverterTest
{
    [TestMethod]
    [TestCategory("Unit")]
    [DataRow("600000", "sh600000")]
    [DataRow("SH600000", "sh600000")]
    [DataRow("sh600000", "sh600000")]
    [DataRow("000001", "sz000001")]
    [DataRow("SZ000001", "sz000001")]
    [DataRow("sz000001", "sz000001")]
    [DataRow("688001", "sh688001")]
    [DataRow("300750", "sz300750")]
    public void ToClsFormat_ShouldReturnCorrectFormat(string input, string expected)
    {
        var result = StockSymbolConverter.ToClsFormat(input);
        Assert.AreEqual(expected, result.ToLowerInvariant());
    }

    /// <summary>
    /// 区域设置回归防护：土耳其语等区域下 "iota".ToUpper() 会得到 "İOTA"（带点大写 I），
    /// 与 "IOTA" 不相等。任何对标的/标识符做区域敏感大小写转换的代码，
    /// 都会把 IOTA、SHIB 之类的符号改坏，进而路由到错误标的。
    /// 这些用例锁定"符号转换与当前区域无关"这一契约，防止后续重构引入 ToUpper()/ToLower()。
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    [DataRow("tr-TR")]
    [DataRow("az-Latn-AZ")]
    [DataRow("en-US")]
    [DataRow("zh-CN")]
    public void ToClsFormat_UnderAnyCulture_ShouldResolveExchangeCorrectly(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            // 这些代码的首位字符在土耳其语区域下大小写映射特殊，最易暴露问题
            Assert.AreEqual("sh688981", StockSymbolConverter.ToClsFormat("688981"), $"[{culture}] 科创板应归上交所");
            Assert.AreEqual("sz000001", StockSymbolConverter.ToClsFormat("000001"), $"[{culture}] 深市主板应归深交所");
            Assert.AreEqual("sz300750", StockSymbolConverter.ToClsFormat("300750"), $"[{culture}] 创业板应归深交所");
            Assert.AreEqual("sh600000", StockSymbolConverter.ToClsFormat("600000"), $"[{culture}] 沪市主板应归上交所");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// 含字母 I 的加密标的在土耳其语区域下最易被区域敏感转换破坏，
    /// 这里显式覆盖以锁定契约（ToBinanceFormat 内部必须使用 Invariant 转换）。
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    [DataRow("tr-TR")]
    [DataRow("az-Latn-AZ")]
    [DataRow("en-US")]
    public void CryptoSymbol_UnderAnyCulture_ShouldPreserveTurkishISensitiveSymbols(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            Assert.AreEqual("IOTA", CryptoSymbolConverter.ExtractBaseCurrency("IOTAUSDT"), $"[{culture}] 基础币种应为 IOTA");
            Assert.AreEqual("IOTAUSDT", CryptoSymbolConverter.ToBinanceFormat("iota"), $"[{culture}] 交易对应为 IOTAUSDT");
            Assert.AreEqual("SHIB", CryptoSymbolConverter.ExtractBaseCurrency("SHIBUSDT"), $"[{culture}] 基础币种应为 SHIB");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    [DataRow("tr-TR")]
    [DataRow("en-US")]
    public void ToZhiTuFormat_UnderAnyCulture_ShouldBeCultureIndependent(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            Assert.AreEqual("600519.SH", StockSymbolConverter.ToZhiTuFormat("600519"), $"[{culture}]");
            Assert.AreEqual("000001.SZ", StockSymbolConverter.ToZhiTuFormat("000001"), $"[{culture}]");
            Assert.AreEqual("600519.SH", StockSymbolConverter.ToZhiTuFormat("SH600519"), $"[{culture}]");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    [DataRow("600000", "600000.SH")]
    [DataRow("000001", "000001.SZ")]
    [DataRow("688001", "688001.SH")]
    [DataRow("300750", "300750.SZ")]
    [DataRow("900901", "900901.SH")]
    [DataRow("601318", "601318.SH")]
    [DataRow("603259", "603259.SH")]
    [DataRow("605116", "605116.SH")]
    [DataRow("000002", "000002.SZ")]
    [DataRow("001979", "001979.SZ")]
    [DataRow("002415", "002415.SZ")]
    [DataRow("003816", "003816.SZ")]
    [DataRow("301021", "301021.SZ")]
    [DataRow("399001", "399001.SZ")]
    public void ToZhiTuFormat_ShouldReturnCorrectFormat_ForNumericCode(string input, string expected)
    {
        var result = StockSymbolConverter.ToZhiTuFormat(input);
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [DataRow("sh600000", "600000.SH")]
    [DataRow("sz000001", "000001.SZ")]
    [DataRow("SH688001", "688001.SH")]
    [DataRow("SZ300750", "300750.SZ")]
    public void ToZhiTuFormat_ShouldReturnCorrectFormat_ForPrefixedCode(string input, string expected)
    {
        var result = StockSymbolConverter.ToZhiTuFormat(input);
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [DataRow("600000.SH", "600000.SH")]
    [DataRow("000001.sz", "000001.SZ")]
    [DataRow("688001.sh", "688001.SH")]
    public void ToZhiTuFormat_ShouldReturnUpperCase_WhenAlreadyInCorrectFormat(string input, string expected)
    {
        var result = StockSymbolConverter.ToZhiTuFormat(input);
        Assert.AreEqual(expected, result);
    }

    /// <summary>
    /// 交易所前缀拆分契约：带前缀符号 → （市场, 代码）的统一入口。
    /// 关键约定：① 大小写不敏感、市场输出恒大写；② 未识别前缀时市场为空、代码原样返回，
    /// 绝不「默认上海」——猜错交易所等于把标的路由到错误市场。
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    [DataRow("SH600000", "SH", "600000")]
    [DataRow("sh600000", "SH", "600000")]
    [DataRow("SZ000001", "SZ", "000001")]
    [DataRow("sz000001", "SZ", "000001")]
    [DataRow("BJ430047", "BJ", "430047")]
    [DataRow("bj430047", "BJ", "430047")]
    [DataRow("600000", "", "600000")]
    [DataRow("600000.SH", "", "600000.SH")]
    [DataRow("XX600000", "", "XX600000")]
    public void SplitExchangePrefix_ShouldFollowUnifiedContract(string input, string expectedMarket, string expectedCode)
    {
        var (market, code) = StockSymbolConverter.SplitExchangePrefix(input);

        Assert.AreEqual(expectedMarket, market);
        Assert.AreEqual(expectedCode, code);
    }

    /// <summary>
    /// 边界形态：空白与过短输入不得抛异常，也不得凭空产出市场归属。
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("SH")]
    [DataRow("sh")]
    public void SplitExchangePrefix_OnBlankOrTooShort_ShouldReturnEmptyMarketAndOriginalCode(string input)
    {
        var (market, code) = StockSymbolConverter.SplitExchangePrefix(input);

        Assert.AreEqual(string.Empty, market);
        Assert.AreEqual(input?.Trim() ?? string.Empty, code);
    }

    /// <summary>
    /// 前缀识别与当前区域无关：tr-TR 等区域下大小写映射特殊，
    /// 而交易所前缀是路由依据，任何区域敏感比较都可能把沪市符号判成未知前缀。
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    [DataRow("tr-TR")]
    [DataRow("en-US")]
    [DataRow("zh-CN")]
    public void SplitExchangePrefix_UnderAnyCulture_ShouldRecognizePrefixes(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            Assert.AreEqual("SH", StockSymbolConverter.SplitExchangePrefix("sh600000").Market, $"[{culture}]");
            Assert.AreEqual("SZ", StockSymbolConverter.SplitExchangePrefix("SZ000001").Market, $"[{culture}]");
            Assert.AreEqual("BJ", StockSymbolConverter.SplitExchangePrefix("bj430047").Market, $"[{culture}]");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
