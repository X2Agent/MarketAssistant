using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Infrastructure.Providers;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using Moq;

namespace TestMarketAssistant.Infrastructure;

[TestClass]
public class ChatClientFactoryTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public void CreateRuntime_SameConfiguration_ShouldReuseClient()
    {
        var fixture = CreateFixture();
        using var factory = fixture.Factory;

        var first = factory.CreateRuntime();
        var second = factory.CreateRuntime();

        Assert.AreSame(first.Client, second.Client);
        Assert.AreEqual(first.ConfigurationFingerprint, second.ConfigurationFingerprint);
        Assert.IsNull(first.ContextWindowTokens);
        Assert.AreEqual(StructuredOutputMode.JsonObject, first.StructuredOutputMode);
        Assert.AreEqual(first.StructuredOutputMode, second.StructuredOutputMode);
        Assert.HasCount(1, fixture.Factory.CreatedRequests);
        Assert.AreEqual(
            new ClientCreationRequest("DeepSeek", "model-a", "secret-1", "https://api.deepseek.com"),
            fixture.Factory.CreatedRequests[0]);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CreateRuntime_ModelChanged_ShouldKeepOldClientAliveUntilFactoryDisposal()
    {
        var fixture = CreateFixture();
        var factory = fixture.Factory;

        var first = factory.CreateRuntime();
        fixture.Setting.ModelId = "model-b";
        var second = factory.CreateRuntime();

        Assert.AreNotSame(first.Client, second.Client);
        Assert.AreNotEqual(first.ConfigurationFingerprint, second.ConfigurationFingerprint);
        fixture.Factory.CreatedClients[0].Verify(x => x.Dispose(), Times.Never);
        fixture.Factory.CreatedClients[1].Verify(x => x.Dispose(), Times.Never);

        factory.Dispose();

        fixture.Factory.CreatedClients[0].Verify(x => x.Dispose(), Times.Once);
        fixture.Factory.CreatedClients[1].Verify(x => x.Dispose(), Times.Once);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CreateRuntime_ApiKeyChanged_ShouldCreateNewRuntimeWithoutExposingSecret()
    {
        var fixture = CreateFixture();
        using var factory = fixture.Factory;

        var first = factory.CreateRuntime();
        fixture.Setting.ProviderApiKeys[fixture.Setting.ProviderId] = "secret-2";
        var second = factory.CreateRuntime();

        Assert.AreNotSame(first.Client, second.Client);
        Assert.AreNotEqual(first.ConfigurationFingerprint, second.ConfigurationFingerprint);
        Assert.IsFalse(first.ConfigurationFingerprint.Contains("secret-1", StringComparison.Ordinal));
        Assert.IsFalse(second.ConfigurationFingerprint.Contains("secret-2", StringComparison.Ordinal));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CreateRuntime_VerifiedModel_ShouldExposeContextWindow()
    {
        var fixture = CreateFixture();
        using var factory = fixture.Factory;
        fixture.Setting.ModelId = "deepseek-v4-flash";

        var runtime = factory.CreateRuntime();

        Assert.AreEqual(1_000_000, runtime.ContextWindowTokens);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CreateRuntime_RemoteModelWithoutApiKey_ShouldExplainProviderAndModelRequirement()
    {
        var fixture = CreateFixture();
        fixture.Setting.ProviderApiKeys.Clear();
        using var factory = fixture.Factory;

        var exception = Assert.ThrowsExactly<FriendlyException>(() => factory.CreateRuntime());

        StringAssert.Contains(exception.Message, "DeepSeek");
        StringAssert.Contains(exception.Message, "model-a");
        StringAssert.Contains(exception.Message, "API Key");
        Assert.HasCount(0, fixture.Factory.CreatedRequests);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CreateRuntime_OpenCodeZenFreeModelWithoutApiKey_ShouldCreateAnonymousClient()
    {
        var fixture = CreateFixture();
        fixture.Setting.ProviderId = "OpenCodeZen";
        fixture.Setting.ModelId = "deepseek-v4-flash-free";
        fixture.Setting.ProviderApiKeys.Clear();
        using var factory = fixture.Factory;

        var runtime = factory.CreateRuntime();

        Assert.AreEqual("OpenCodeZen", runtime.ProviderId);
        Assert.AreEqual(string.Empty, fixture.Factory.CreatedRequests.Single().ApiKey);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CreateRuntime_OpenCodeZenFreeModelWithStoredKey_ShouldInjectKey()
    {
        var fixture = CreateFixture();
        fixture.Setting.ProviderId = "OpenCodeZen";
        fixture.Setting.ModelId = "deepseek-v4-flash-free";
        fixture.Setting.ProviderApiKeys["OpenCodeZen"] = "configured-key";
        using var factory = fixture.Factory;

        factory.CreateRuntime();

        Assert.AreEqual("configured-key", fixture.Factory.CreatedRequests.Single().ApiKey);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CreateRuntime_UnsupportedOpenCodeZenProtocol_ShouldFailBeforeClientCreation()
    {
        var fixture = CreateFixture();
        fixture.Setting.ProviderId = "OpenCodeZen";
        fixture.Setting.ModelId = "gpt-5.6-sol";
        fixture.Setting.ProviderApiKeys["OpenCodeZen"] = "secret";
        using var factory = fixture.Factory;

        var exception = Assert.ThrowsExactly<FriendlyException>(() => factory.CreateRuntime());

        StringAssert.Contains(exception.Message, "API 协议尚未接入");
        Assert.HasCount(0, fixture.Factory.CreatedRequests);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CreateRuntime_AfterDisposal_ShouldThrow()
    {
        var fixture = CreateFixture();
        fixture.Factory.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Factory.CreateRuntime());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CreateRuntime_MoreThanSixteenConfigurations_ShouldRejectWithoutEvictingLiveClients()
    {
        var fixture = CreateFixture();
        using var factory = fixture.Factory;

        for (var index = 0; index < 16; index++)
        {
            fixture.Setting.ModelId = $"model-{index}";
            factory.CreateRuntime();
        }

        fixture.Setting.ModelId = "model-16";
        var exception = Assert.ThrowsExactly<FriendlyException>(() => factory.CreateRuntime());

        StringAssert.Contains(exception.Message, "16");
        Assert.HasCount(16, fixture.Factory.CreatedClients);
        foreach (var client in fixture.Factory.CreatedClients)
            client.Verify(x => x.Dispose(), Times.Never);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CreateRuntime_FixedEndpointProviderWithStoredOverride_ShouldIgnoreOverride()
    {
        var fixture = CreateFixture();
        fixture.Setting.Endpoint = "https://proxy.example.com/v1/";
        using var factory = fixture.Factory;

        factory.CreateRuntime();

        Assert.AreEqual(
            "https://api.deepseek.com",
            fixture.Factory.CreatedRequests.Single().Endpoint);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CreateRuntime_OllamaWithoutConfiguredEndpoint_ShouldUseOfficialDefaultEndpoint()
    {
        var fixture = CreateFixture();
        fixture.Setting.ProviderId = "Ollama";
        fixture.Setting.ModelId = "qwen3:8b";
        fixture.Setting.Endpoint = string.Empty;
        fixture.Setting.ProviderApiKeys.Clear();
        using var factory = fixture.Factory;

        factory.CreateRuntime();

        Assert.AreEqual(
            "http://localhost:11434",
            fixture.Factory.CreatedRequests.Single().Endpoint);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CreateRuntime_CustomProviderWithoutEndpoint_ShouldReturnFriendlyConfigurationError()
    {
        var fixture = CreateFixture();
        fixture.Setting.ProviderId = "Custom";
        fixture.Setting.ModelId = "model-a";
        fixture.Setting.Endpoint = string.Empty;
        fixture.Setting.ProviderApiKeys["Custom"] = "secret";
        using var factory = fixture.Factory;

        var exception = Assert.ThrowsExactly<FriendlyException>(() => factory.CreateRuntime());

        StringAssert.Contains(exception.Message, "API Base URL");
        Assert.HasCount(0, fixture.Factory.CreatedRequests);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CreateRuntime_OpenAIModernModel_ShouldExposeJsonSchemaMode()
    {
        var fixture = CreateFixture();
        fixture.Setting.ProviderId = "OpenAI";
        fixture.Setting.ModelId = "gpt-4o-mini";
        fixture.Setting.Endpoint = "https://api.openai.com/v1";
        fixture.Setting.ProviderApiKeys["OpenAI"] = "secret";
        using var factory = fixture.Factory;

        var runtime = factory.CreateRuntime();

        Assert.AreEqual(StructuredOutputMode.JsonSchema, runtime.StructuredOutputMode);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CreateRuntime_CustomEndpoint_ShouldExposeTextMode()
    {
        var fixture = CreateFixture();
        fixture.Setting.ProviderId = "Custom";
        fixture.Setting.ModelId = "unknown-model";
        fixture.Setting.Endpoint = "https://custom.example.com/v1";
        fixture.Setting.ProviderApiKeys["Custom"] = "secret";
        using var factory = fixture.Factory;

        var runtime = factory.CreateRuntime();

        Assert.AreEqual(StructuredOutputMode.Text, runtime.StructuredOutputMode);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CreateRuntime_LocalProviderWithoutApiKey_ShouldCreateClient()
    {
        var fixture = CreateFixture();
        fixture.Setting.ProviderId = "Ollama";
        fixture.Setting.ModelId = "qwen3:8b";
        fixture.Setting.Endpoint = "http://localhost:11434";
        fixture.Setting.ProviderApiKeys.Clear();
        using var factory = fixture.Factory;

        var runtime = factory.CreateRuntime();

        Assert.AreEqual("Ollama", runtime.ProviderId);
        Assert.AreEqual("qwen3:8b", runtime.ModelId);
        Assert.AreEqual(StructuredOutputMode.JsonObject, runtime.StructuredOutputMode);
        Assert.AreEqual(
            new ClientCreationRequest("Ollama", "qwen3:8b", string.Empty, "http://localhost:11434"),
            fixture.Factory.CreatedRequests.Single());
    }

    private static Fixture CreateFixture()
    {
        var setting = new UserSetting
        {
            ProviderId = "DeepSeek",
            ModelId = "model-a",
            Endpoint = "https://api.deepseek.com",
            ProviderApiKeys = new Dictionary<string, string>
            {
                ["DeepSeek"] = "secret-1"
            }
        };

        var settingService = new Mock<IUserSettingService>();
        settingService.SetupGet(x => x.CurrentSetting).Returns(setting);
        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(new HttpClient());

        return new Fixture(
            new TestableChatClientFactory(settingService.Object, httpClientFactory.Object),
            setting);
    }

    private sealed record Fixture(TestableChatClientFactory Factory, UserSetting Setting);

    private sealed class TestableChatClientFactory(
        IUserSettingService userSettingService,
        IHttpClientFactory httpClientFactory)
        : ChatClientFactory(userSettingService, httpClientFactory)
    {
        public List<ClientCreationRequest> CreatedRequests { get; } = [];
        public List<Mock<IChatClient>> CreatedClients { get; } = [];

        protected override IChatClient CreateClient(
            ModelProvider provider,
            string modelId,
            string apiKey,
            string endpoint)
        {
            CreatedRequests.Add(new ClientCreationRequest(provider.Id, modelId, apiKey, endpoint));
            var client = new Mock<IChatClient>();
            CreatedClients.Add(client);
            return client.Object;
        }
    }
}

internal sealed record ClientCreationRequest(
    string ProviderId,
    string ModelId,
    string ApiKey,
    string Endpoint);
