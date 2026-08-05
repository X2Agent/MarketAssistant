using System.Net;
using System.Text;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Infrastructure.Providers;
using MarketAssistant.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Moq;

namespace TestMarketAssistant.Infrastructure;

[TestClass]
public class ModelDiscoveryServiceTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public async Task ListModelsAsync_AnonymousProvider_ShouldRemoveAuthorizationAndFilterUnsupportedProtocols()
    {
        var handler = new RecordingHandler(
            """
            {
              "object": "list",
              "data": [
                { "id": "deepseek-v4-flash-free", "object": "model", "created": 0, "owned_by": "opencode" },
                { "id": "gpt-5.6-sol", "object": "model", "created": 0, "owned_by": "opencode" },
                { "id": "kimi-k3", "object": "model", "created": 0, "owned_by": "opencode" },
                { "id": "future-unknown-model", "object": "model", "created": 0, "owned_by": "opencode" }
              ]
            }
            """);
        var httpClientFactory = CreateHttpClientFactory(handler);
        var service = new ModelDiscoveryService(httpClientFactory.Object);
        var provider = ModelProviderCatalog.GetProvider("OpenCodeZen")!;

        var models = await service.ListModelsAsync(provider, null);

        CollectionAssert.AreEqual(
            new[] { "deepseek-v4-flash-free", "kimi-k3" },
            models.ToArray());
        Assert.IsNotNull(handler.Request);
        Assert.IsNull(handler.Request.Headers.Authorization);
        Assert.AreEqual("https://opencode.ai/zen/v1/models", handler.Request.RequestUri?.AbsoluteUri);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task AnonymousOpenAIHttpClient_ShouldNotAddHttpResilienceRetries()
    {
        var handler = new CountingHandler(HttpStatusCode.InternalServerError);
        var services = new ServiceCollection();
        services.AddNamedMarketHttpClients();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter>(
            new ReplacePrimaryHandlerFilter("AnonymousOpenAI", handler));
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("AnonymousOpenAI");

        using var response = await client.GetAsync("https://example.invalid/models");

        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.AreEqual(1, handler.CallCount);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task ListModelsAsync_FixedEndpointProvider_ShouldIgnoreEndpointOverride()
    {
        var handler = new RecordingHandler(
            """
            { "object": "list", "data": [] }
            """);
        var service = new ModelDiscoveryService(CreateHttpClientFactory(handler).Object);
        var provider = ModelProviderCatalog.GetProvider("OpenCodeZen")!;

        await service.ListModelsAsync(provider, null, "https://proxy.example.com/v1/");

        Assert.AreEqual("https://opencode.ai/zen/v1/models", handler.Request?.RequestUri?.AbsoluteUri);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task ListModelsAsync_ProviderRequiringKeyWithoutKey_ShouldFailBeforeNetworkRequest()
    {
        var handler = new RecordingHandler("{}");
        var service = new ModelDiscoveryService(CreateHttpClientFactory(handler).Object);
        var provider = ModelProviderCatalog.GetProvider("DeepSeek")!;

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.ListModelsAsync(provider, null));

        StringAssert.Contains(exception.Message, "API Key");
        Assert.IsNull(handler.Request);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task ListModelsAsync_LocalProvider_ShouldHonorEndpointOverride()
    {
        var handler = new RecordingHandler(
            """
            { "object": "list", "data": [] }
            """);
        var service = new ModelDiscoveryService(CreateHttpClientFactory(handler).Object);
        var provider = ModelProviderCatalog.GetProvider("LMStudio")!;

        await service.ListModelsAsync(provider, null, "http://localhost:2234/v1/");

        Assert.AreEqual("http://localhost:2234/v1/models", handler.Request?.RequestUri?.AbsoluteUri);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task ListModelsAsync_LmStudioWithoutKey_ShouldUseAnonymousDefaultEndpoint()
    {
        var handler = new RecordingHandler(
            """
            { "object": "list", "data": [{ "id": "local-model", "object": "model", "created": 0, "owned_by": "local" }] }
            """);
        var service = new ModelDiscoveryService(CreateHttpClientFactory(handler).Object);
        var provider = ModelProviderCatalog.GetProvider("LMStudio")!;

        var models = await service.ListModelsAsync(provider, null);

        CollectionAssert.AreEqual(new[] { "local-model" }, models.ToArray());
        Assert.IsNull(handler.Request?.Headers.Authorization);
        Assert.AreEqual("http://localhost:1234/v1/models", handler.Request?.RequestUri?.AbsoluteUri);
    }

    private static Mock<IHttpClientFactory> CreateHttpClientFactory(HttpMessageHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory
            .Setup(x => x.CreateClient("AnonymousOpenAI"))
            .Returns(new HttpClient(handler, disposeHandler: false));
        return factory;
    }

    private sealed class ReplacePrimaryHandlerFilter(
        string clientName,
        HttpMessageHandler handler) : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
        {
            return builder =>
            {
                next(builder);
                if (builder.Name == clientName)
                {
                    builder.PrimaryHandler = handler;
                    builder.AdditionalHandlers.Clear();
                }
            };
        }
    }

    private sealed class CountingHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                RequestMessage = request
            });
        }
    }

    private sealed class RecordingHandler(string responseJson) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
                RequestMessage = request
            });
        }
    }
}
