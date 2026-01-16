using MarketAssistant.Infrastructure.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestMarketAssistant;

/// <summary>
/// CryptoSymbolConverter 工具类单元测试
/// </summary>
[TestClass]
public class CryptoSymbolConverterTest
{
    [TestMethod]
    public void ToBinanceFormat_SimpleCoin_ShouldAddUSDT()
    {
        // Arrange & Act
        var result = CryptoSymbolConverter.ToBinanceFormat("BTC");

        // Assert
        Assert.AreEqual("BTCUSDT", result);
    }

    [TestMethod]
    public void ToBinanceFormat_WithCryptoPrefix_ShouldRemovePrefix()
    {
        // Arrange & Act
        var result = CryptoSymbolConverter.ToBinanceFormat("crypto.ETH");

        // Assert
        Assert.AreEqual("ETHUSDT", result);
    }

    [TestMethod]
    public void ToBinanceFormat_AlreadyHasUSDT_ShouldReturnAsIs()
    {
        // Arrange & Act
        var result = CryptoSymbolConverter.ToBinanceFormat("BTCUSDT");

        // Assert
        Assert.AreEqual("BTCUSDT", result);
    }

    [TestMethod]
    public void ToBinanceFormat_WithSlash_ShouldRemoveSlash()
    {
        // Arrange & Act
        var result = CryptoSymbolConverter.ToBinanceFormat("BTC/USDT");

        // Assert
        Assert.AreEqual("BTCUSDT", result);
    }

    [TestMethod]
    public void ToBinanceFormat_WithCustomQuote_ShouldUseCustomQuote()
    {
        // Arrange & Act
        var result = CryptoSymbolConverter.ToBinanceFormat("ETH", "BTC");

        // Assert
        Assert.AreEqual("ETHBTC", result);
    }

    [TestMethod]
    public void ExtractBaseCurrency_FromBTCUSDT_ShouldReturnBTC()
    {
        // Arrange & Act
        var result = CryptoSymbolConverter.ExtractBaseCurrency("BTCUSDT");

        // Assert
        Assert.AreEqual("BTC", result);
    }

    [TestMethod]
    public void ExtractBaseCurrency_FromETHBTC_ShouldReturnETH()
    {
        // Arrange & Act
        var result = CryptoSymbolConverter.ExtractBaseCurrency("ETHBTC");

        // Assert
        Assert.AreEqual("ETH", result);
    }

    [TestMethod]
    public void ExtractBaseCurrency_FromBNBBUSD_ShouldReturnBNB()
    {
        // Arrange & Act
        var result = CryptoSymbolConverter.ExtractBaseCurrency("BNBBUSD");

        // Assert
        Assert.AreEqual("BNB", result);
    }

    [TestMethod]
    public void ToCoinGeckoId_BTC_ShouldReturnBitcoin()
    {
        // Arrange & Act
        var result = CryptoSymbolConverter.ToCoinGeckoId("BTC");

        // Assert
        Assert.AreEqual("bitcoin", result);
    }

    [TestMethod]
    public void ToCoinGeckoId_BTCUSDT_ShouldReturnBitcoin()
    {
        // Arrange & Act
        var result = CryptoSymbolConverter.ToCoinGeckoId("BTCUSDT");

        // Assert
        Assert.AreEqual("bitcoin", result);
    }

    [TestMethod]
    public void ToCoinGeckoId_ETH_ShouldReturnEthereum()
    {
        // Arrange & Act
        var result = CryptoSymbolConverter.ToCoinGeckoId("ETH");

        // Assert
        Assert.AreEqual("ethereum", result);
    }

    [TestMethod]
    public void ToCoinGeckoId_UnknownCoin_ShouldReturnLowercase()
    {
        // Arrange & Act
        var result = CryptoSymbolConverter.ToCoinGeckoId("NEWCOIN");

        // Assert
        Assert.AreEqual("newcoin", result);
    }

    [TestMethod]
    public void ToBinanceFormat_EmptyString_ShouldThrowException()
    {
        // Arrange & Act & Assert
        Assert.ThrowsExactly<ArgumentException>(() => CryptoSymbolConverter.ToBinanceFormat(""));
    }

    [TestMethod]
    public void ToBinanceFormat_NullString_ShouldThrowException()
    {
        // Arrange & Act & Assert
        Assert.ThrowsExactly<ArgumentException>(() => CryptoSymbolConverter.ToBinanceFormat(null!));
    }
}
