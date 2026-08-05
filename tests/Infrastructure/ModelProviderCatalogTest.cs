using MarketAssistant.Infrastructure.Core;
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
        Assert.IsTrue(GetProvider("Ollama").CanListModels(null));
        Assert.IsTrue(GetProvider("LMStudio").CanListModels(null));
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
    public void ModelApiKeyRequirement_ShouldUseProviderPolicy()
    {
        var remoteProvider = new ModelProvider(
            Id: "Remote",
            DisplayName: "Remote",
            DefaultEndpoint: "https://example.com/v1",
            ApiKeyUrl: null);

        Assert.IsTrue(remoteProvider.RequiresApiKeyForModel("paid-model"));
        Assert.IsTrue(remoteProvider.RequiresApiKeyForModel("model-free"));
        Assert.IsTrue(remoteProvider.RequiresApiKeyForModel(null));
        Assert.IsFalse(GetProvider("Ollama").RequiresApiKeyForModel("any-local-model"));
        Assert.IsTrue(GetProvider("Qwen").RequiresApiKeyForModel("qwen-model-free"));
        Assert.AreEqual(ModelApiProtocol.OpenAIChatCompletions, GetProvider("Qwen").GetProtocol("qwen3.7-plus"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void EndpointOverride_ShouldBeLimitedToLocalAndCustomProviders()
    {
        var overrideProviderIds = ModelProviderCatalog.Providers
            .Where(provider => provider.AllowsEndpointOverride)
            .Select(provider => provider.Id)
            .OrderBy(id => id)
            .ToArray();

        CollectionAssert.AreEqual(new[] { "Custom", "LMStudio", "Ollama" }, overrideProviderIds);
        Assert.AreEqual("http://localhost:11434", GetProvider("Ollama").DefaultEndpoint);
        Assert.AreEqual("http://localhost:1234/v1", GetProvider("LMStudio").DefaultEndpoint);
        Assert.AreEqual(string.Empty, GetProvider("Custom").DefaultEndpoint);
        Assert.IsFalse(GetProvider("OpenCodeZen").AllowsEndpointOverride);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void GetProvider_ShouldIgnoreIdentifierCase()
    {
        Assert.AreSame(GetProvider("DeepSeek"), ModelProviderCatalog.GetProvider("deepseek"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ProviderProtocol_ShouldMatchOfficialSdkClient()
    {
        Assert.AreEqual(ModelApiProtocol.Ollama, GetProvider("Ollama").Protocol);

        foreach (var provider in ModelProviderCatalog.Providers.Where(provider => provider.Id != "Ollama"))
        {
            Assert.AreEqual(
                ModelApiProtocol.OpenAIChatCompletions,
                provider.Protocol,
                $"{provider.Id} 默认应使用 OpenAI Chat Completions");
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
    public void OpenCodeZen_ShouldSelectProtocolPerModel()
    {
        var provider = GetProvider("OpenCodeZen");

        Assert.IsInstanceOfType<OpenCodeZenModelPolicy>(provider.Policy);
        Assert.AreEqual(ModelApiProtocol.OpenAIChatCompletions, provider.GetProtocol("deepseek-v4-flash"));
        Assert.AreEqual(ModelApiProtocol.OpenAIChatCompletions, provider.GetProtocol("kimi-k3"));
        Assert.AreEqual(ModelApiProtocol.OpenAIChatCompletions, provider.GetProtocol("big-pickle"));
        Assert.AreEqual(ModelApiProtocol.Unsupported, provider.GetProtocol("grok-4.5"));
        Assert.AreEqual(ModelApiProtocol.Unsupported, provider.GetProtocol("gpt-5.6-sol"));
        Assert.AreEqual(ModelApiProtocol.Unsupported, provider.GetProtocol("claude-sonnet-5"));
        Assert.AreEqual(ModelApiProtocol.Unsupported, provider.GetProtocol("gemini-3.6-flash"));
        Assert.AreEqual(ModelApiProtocol.Unsupported, provider.GetProtocol("qwen3.7-plus"));
        Assert.AreEqual(ModelApiProtocol.Unsupported, provider.GetProtocol("future-unknown-model"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void OpenCodeZen_ShouldUseFreeSuffixForAnonymousModels()
    {
        var provider = GetProvider("OpenCodeZen");

        Assert.IsTrue(provider.RequiresApiKey);
        Assert.IsFalse(provider.RequiresApiKeyForModel("deepseek-v4-flash-free"));
        Assert.IsFalse(provider.RequiresApiKeyForModel("MIMO-V2.5-FREE"));
        Assert.IsFalse(provider.RequiresApiKeyForModel("big-pickle"));
        Assert.IsTrue(provider.RequiresApiKeyForModel("deepseek-v4-flash"));
        Assert.IsTrue(provider.RequiresApiKeyForModel("deepseek-v4-flash-free-preview"));
        Assert.IsFalse(provider.ModelListingRequiresApiKey);
        Assert.IsTrue(provider.CanListModels(null));
        Assert.IsFalse(GetProvider("DeepSeek").CanListModels(null));
        Assert.IsTrue(GetProvider("DeepSeek").CanListModels("configured-key"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void StructuredOutputMode_ShouldBeConfiguredPerProvider()
    {
        Assert.AreEqual(StructuredOutputMode.JsonSchema, GetProvider("OpenAI").StructuredOutputMode);
        Assert.AreEqual(StructuredOutputMode.Text, GetProvider("Custom").StructuredOutputMode);

        foreach (var provider in ModelProviderCatalog.Providers.Where(provider => provider.Id is not ("OpenAI" or "Custom")))
        {
            Assert.AreEqual(
                StructuredOutputMode.JsonObject,
                provider.StructuredOutputMode,
                $"服务商 {provider.Id} 应使用兼容性更广的 json_object");
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CustomProvider_ShouldUseTextFallback()
    {
        Assert.IsFalse(GetProvider("Custom").SupportsModelListing);
        Assert.AreEqual(StructuredOutputMode.Text, GetProvider("Custom").StructuredOutputMode);
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
