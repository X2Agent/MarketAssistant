using System.Text.Json;
using MarketAssistant.Applications.Settings;

namespace TestMarketAssistant.Infrastructure;

[TestClass]
public class SecretSerializationTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public void UserSetting_Serialization_ShouldExcludeAllSecrets()
    {
        var setting = new UserSetting
        {
            ProviderApiKeys = new Dictionary<string, string> { ["DeepSeek"] = "provider-secret" },
            EmbeddingApiKey = "embedding-secret",
            ZhiTuApiToken = "zhitu-secret",
            CoinGeckoApiKey = "coingecko-secret",
            BinanceApiKey = "binance-key",
            BinanceSecretKey = "binance-secret",
            WebSearchApiKey = "search-secret"
        };

        var json = JsonSerializer.Serialize(setting);

        Assert.IsFalse(json.Contains("provider-secret", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("embedding-secret", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("zhitu-secret", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("coingecko-secret", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("binance-key", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("binance-secret", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("search-secret", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains(nameof(UserSetting.ProviderApiKeys), StringComparison.Ordinal));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void McpServerConfig_Serialization_ShouldExcludeEnvironmentVariables()
    {
        var config = new MCPServerConfig
        {
            Id = "server-1",
            Name = "test",
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["API_TOKEN"] = "mcp-secret"
            }
        };

        var json = JsonSerializer.Serialize(config);

        Assert.IsFalse(json.Contains("mcp-secret", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains(nameof(MCPServerConfig.EnvironmentVariables), StringComparison.Ordinal));
    }
}
