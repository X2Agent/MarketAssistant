using MarketAssistant.Infrastructure.Providers;

namespace TestMarketAssistant.Infrastructure;

[TestClass]
public class ModelProviderCatalogTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public void Providers_ShouldHaveUniqueIdentifiersAndAbsoluteEndpoints()
    {
        var duplicateIds = ModelProviderCatalog.Providers
            .GroupBy(provider => provider.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.HasCount(0, duplicateIds);

        foreach (var provider in ModelProviderCatalog.Providers
            .Where(provider => !string.IsNullOrWhiteSpace(provider.DefaultEndpoint)))
        {
            Assert.IsTrue(
                Uri.TryCreate(provider.DefaultEndpoint, UriKind.Absolute, out var endpoint) &&
                endpoint.Scheme is "http" or "https",
                $"{provider.Id} 的默认 Endpoint 不是绝对 HTTP/HTTPS 地址: {provider.DefaultEndpoint}");
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void LocalProviders_ShouldNotRequireApiKey()
    {
        Assert.IsFalse(GetProvider("Ollama").RequiresApiKey);
        Assert.IsFalse(GetProvider("LMStudio").RequiresApiKey);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void RemoteProviders_ShouldRequireApiKey()
    {
        foreach (var provider in ModelProviderCatalog.Providers.Where(provider =>
                     provider.Id is not ("Ollama" or "LMStudio")))
        {
            Assert.IsTrue(provider.RequiresApiKey, $"远程服务商 {provider.Id} 必须要求 API Key");
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ModelApiKeyRequirement_ShouldUseProviderAndModelScope()
    {
        var remoteProvider = new ModelProvider(
            Id: "Remote",
            DisplayName: "Remote",
            DefaultEndpoint: "https://example.com/v1",
            ApiKeyUrl: null,
            ApiKeyOptionalModelIds: ["anonymous-model"]);

        Assert.IsTrue(remoteProvider.RequiresApiKeyForModel("paid-model"));
        Assert.IsFalse(remoteProvider.RequiresApiKeyForModel("ANONYMOUS-MODEL"));
        Assert.IsTrue(remoteProvider.RequiresApiKeyForModel(null));
        Assert.IsFalse(GetProvider("Ollama").RequiresApiKeyForModel("any-local-model"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ProvidersWithEmptyDefaultEndpoint_ShouldBeLimitedToLocalAndCustom()
    {
        var emptyEndpointIds = ModelProviderCatalog.Providers
            .Where(provider => string.IsNullOrWhiteSpace(provider.DefaultEndpoint))
            .Select(provider => provider.Id)
            .OrderBy(id => id)
            .ToArray();

        CollectionAssert.AreEqual(
            new[] { "Custom", "LMStudio", "Ollama" },
            emptyEndpointIds);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AdapterKind_ShouldMatchProviderProtocol()
    {
        Assert.AreEqual(ProviderAdapterKind.Ollama, GetProvider("Ollama").AdapterKind);

        foreach (var provider in ModelProviderCatalog.Providers.Where(provider => provider.Id != "Ollama"))
        {
            Assert.AreEqual(
                ProviderAdapterKind.OpenAICompatible,
                provider.AdapterKind,
                $"{provider.Id} 应使用 OpenAI 兼容适配器");
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Ppio_ShouldUseDocumentedV1BaseEndpoint()
    {
        Assert.AreEqual("https://api.ppio.com/openai/v1", GetProvider("PPIO").DefaultEndpoint);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void OpenCodeZen_ShouldExposeOnlyChatCompletionsCompatibleModels()
    {
        var provider = GetProvider("OpenCodeZen");

        Assert.IsTrue(provider.IsModelSupported("deepseek-v4-flash"));
        Assert.IsTrue(provider.IsModelSupported("grok-4.5"));
        Assert.IsTrue(provider.IsModelSupported("kimi-k3"));
        Assert.IsFalse(provider.IsModelSupported("gpt-5.6-sol"));
        Assert.IsFalse(provider.IsModelSupported("claude-sonnet-5"));
        Assert.IsFalse(provider.IsModelSupported("gemini-3.6-flash"));
        Assert.IsFalse(provider.IsModelSupported("qwen3.7-plus"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CustomProvider_ShouldNotClaimModelListingSupport()
    {
        Assert.IsFalse(GetProvider("Custom").SupportsModelListing);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DeepSeek_ShouldExposeVerifiedModelContextWindowsOnly()
    {
        var provider = GetProvider("DeepSeek");

        Assert.AreEqual(1_000_000, provider.GetContextWindowTokens("deepseek-v4-flash"));
        Assert.AreEqual(1_000_000, provider.GetContextWindowTokens("DEEPSEEK-V4-PRO"));
        Assert.IsNull(provider.GetContextWindowTokens("unverified-model"));
    }

    private static ModelProvider GetProvider(string id)
    {
        return ModelProviderCatalog.GetProvider(id)
            ?? throw new AssertFailedException($"未找到服务商 {id}");
    }
}
