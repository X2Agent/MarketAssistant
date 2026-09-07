using MarketAssistant.DataProviders.Web3;
using MarketAssistant.Infrastructure.Core;

namespace TestMarketAssistant.DataProviders;

/// <summary>
/// ChainRegistry 链 ID 归一化单元测试。
/// </summary>
[TestClass]
public class ChainRegistryTest
{
    [DataTestMethod]
    [DataRow("ethereum", "ethereum")]
    [DataRow("eth", "ethereum")]
    [DataRow("ETH", "ethereum")]
    [DataRow("1", "ethereum")]
    [DataRow("eth-mainnet", "ethereum")]
    [DataRow("binance-smart-chain", "bsc")]
    [DataRow("binance_smart_chain", "bsc")]
    [DataRow("bnb", "bsc")]
    [DataRow("56", "bsc")]
    [DataRow("polygon-pos", "polygon")]
    [DataRow("matic-network", "polygon")]
    [DataRow("137", "polygon")]
    [DataRow("arbitrum-one", "arbitrum")]
    [DataRow("arb", "arbitrum")]
    [DataRow("optimistic-ethereum", "optimism")]
    [DataRow("10", "optimism")]
    [DataRow("avalanche-2", "avalanche")]
    [DataRow("avax", "avalanche")]
    [DataRow("8453", "base")]
    [DataRow("sol", "solana")]
    [DataRow("mainnet", "solana")]
    public void Normalize_KnownAliases_ReturnCanonical(string input, string expected)
    {
        Assert.AreEqual(expected, ChainRegistry.Normalize(input));
    }

    [TestMethod]
    public void Normalize_UnknownChain_PassesThroughLowercase()
    {
        Assert.AreEqual("zksync", ChainRegistry.Normalize("ZKSYNC"));
        Assert.AreEqual("linea", ChainRegistry.Normalize(" linea "));
    }

    [TestMethod]
    public void Normalize_EmptyInput_ThrowsFriendly()
    {
        Assert.ThrowsExactly<FriendlyException>(() => ChainRegistry.Normalize(""));
        Assert.ThrowsExactly<FriendlyException>(() => ChainRegistry.Normalize("  "));
        Assert.ThrowsExactly<FriendlyException>(() => ChainRegistry.Normalize(null));
    }

    [DataTestMethod]
    [DataRow("ethereum", "1")]
    [DataRow("bsc", "56")]
    [DataRow("polygon", "137")]
    [DataRow("arbitrum", "42161")]
    [DataRow("optimism", "10")]
    [DataRow("avalanche", "43114")]
    [DataRow("base", "8453")]
    public void ToGoPlusChainId_EvmChains_ReturnNumericId(string canonical, string expected)
    {
        Assert.AreEqual(expected, ChainRegistry.ToGoPlusChainId(canonical));
    }

    [TestMethod]
    public void ToGoPlusChainId_SolanaAndUnknownChain_ReturnNull()
    {
        Assert.IsNull(ChainRegistry.ToGoPlusChainId("solana"));
        Assert.IsNull(ChainRegistry.ToGoPlusChainId("zksync"));
    }
}