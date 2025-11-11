using MarketAssistant.Infrastructure.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests;

[TestClass]
public class StockSymbolConverterTest
{
    [TestMethod]
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
        Assert.AreEqual(expected, result.ToLower());
    }

    [TestMethod]
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
    [DataRow("600000.SH", "600000.SH")]
    [DataRow("000001.sz", "000001.SZ")]
    [DataRow("688001.sh", "688001.SH")]
    public void ToZhiTuFormat_ShouldReturnUpperCase_WhenAlreadyInCorrectFormat(string input, string expected)
    {
        var result = StockSymbolConverter.ToZhiTuFormat(input);
        Assert.AreEqual(expected, result);
    }
}
