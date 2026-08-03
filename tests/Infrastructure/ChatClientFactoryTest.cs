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
        fixture.AdapterFactory.Verify(x => x.Create(It.IsAny<ModelProvider>()), Times.Once);
        fixture.Adapter.Verify(
            x => x.CreateChatClient("secret-1", "model-a", "https://api.deepseek.com"),
            Times.Once);
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
        fixture.CreatedClients[0].Verify(x => x.Dispose(), Times.Never);
        fixture.CreatedClients[1].Verify(x => x.Dispose(), Times.Never);

        factory.Dispose();

        fixture.CreatedClients[0].Verify(x => x.Dispose(), Times.Once);
        fixture.CreatedClients[1].Verify(x => x.Dispose(), Times.Once);
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
        Assert.AreEqual(16, fixture.CreatedClients.Count);
        foreach (var client in fixture.CreatedClients)
            client.Verify(x => x.Dispose(), Times.Never);
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
        fixture.Adapter.Verify(
            x => x.CreateChatClient(string.Empty, "qwen3:8b", "http://localhost:11434"),
            Times.Once);
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

        var createdClients = new List<Mock<IChatClient>>();
        var adapter = new Mock<IModelProviderAdapter>();
        adapter
            .Setup(x => x.CreateChatClient(
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<string?>()))
            .Returns(() =>
            {
                var client = new Mock<IChatClient>();
                createdClients.Add(client);
                return client.Object;
            });

        var adapterFactory = new Mock<IModelProviderAdapterFactory>();
        adapterFactory
            .Setup(x => x.Create(It.IsAny<ModelProvider>()))
            .Returns(adapter.Object);

        return new Fixture(
            new ChatClientFactory(settingService.Object, adapterFactory.Object),
            setting,
            adapterFactory,
            adapter,
            createdClients);
    }

    private sealed record Fixture(
        ChatClientFactory Factory,
        UserSetting Setting,
        Mock<IModelProviderAdapterFactory> AdapterFactory,
        Mock<IModelProviderAdapter> Adapter,
        List<Mock<IChatClient>> CreatedClients);
}
