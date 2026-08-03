using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Mcp;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace TestMarketAssistant;

[TestClass]
public sealed class McpServiceRuntimeTest
{
    [TestMethod]
    public void ComputeConfigurationFingerprint_IgnoresPolicyOnlyFields()
    {
        var first = CreateConfig();
        var second = CreateConfig();
        second.Id = "different-id";
        second.Description = "different-description";
        second.Category = "different-category";
        second.AllowedTools = ["other-tool"];
        second.IsEnabled = false;

        Assert.AreEqual(
            McpService.ComputeConfigurationFingerprint(first),
            McpService.ComputeConfigurationFingerprint(second));
    }

    [TestMethod]
    public void ComputeConfigurationFingerprint_ChangesWhenConnectionSecretChanges()
    {
        var first = CreateConfig();
        var second = CreateConfig();
        second.EnvironmentVariables["API_KEY"] = "new-secret";

        Assert.AreNotEqual(
            McpService.ComputeConfigurationFingerprint(first),
            McpService.ComputeConfigurationFingerprint(second));
    }

    [TestMethod]
    public async Task GetAITools_SameConnectionConfiguration_ReusesClientAndReappliesAllowList()
    {
        var factory = new FakeMcpClientSessionFactory();
        await using var service = CreateService(factory);
        var config = CreateConfig();
        config.AllowedTools = ["search"];

        var first = await service.GetAIToolsAsync([config]);
        config.AllowedTools = ["trade"];
        var second = await service.GetAIToolsAsync([config]);

        Assert.AreEqual(1, factory.CreateCount);
        Assert.AreEqual(1, service.ActiveConnectionCount);
        Assert.HasCount(1, first);
        Assert.HasCount(1, second);
        Assert.AreEqual("search", first[0].Name);
        Assert.AreEqual("trade", second[0].Name);
    }

    [TestMethod]
    public async Task ResetConnections_KeepsOldClientAliveAndCreatesNewRuntime()
    {
        var factory = new FakeMcpClientSessionFactory();
        await using var service = CreateService(factory);
        var config = CreateConfig();

        await service.GetAIToolsAsync([config]);
        Assert.HasCount(1, factory.Sessions);
        var firstSession = factory.Sessions[0];

        await service.ResetConnectionsAsync();

        Assert.AreEqual(0, service.ActiveConnectionCount);
        Assert.AreEqual(0, firstSession.DisposeCount);

        await service.GetAIToolsAsync([config]);

        Assert.AreEqual(2, factory.CreateCount);
        Assert.AreEqual(1, service.ActiveConnectionCount);
        Assert.AreEqual(0, firstSession.DisposeCount);
    }

    [TestMethod]
    public async Task GetAITools_ListFailure_DisposesFailedClientAndDoesNotCacheIt()
    {
        var factory = new FakeMcpClientSessionFactory(failFirstList: true);
        await using var service = CreateService(factory);
        var config = CreateConfig();

        var first = await service.GetAIToolsAsync([config]);
        Assert.HasCount(1, factory.Sessions);
        var failedSession = factory.Sessions[0];

        Assert.HasCount(0, first);
        Assert.AreEqual(1, failedSession.DisposeCount);
        Assert.AreEqual(0, service.ActiveConnectionCount);

        var second = await service.GetAIToolsAsync([config]);

        Assert.HasCount(2, second);
        Assert.AreEqual(2, factory.CreateCount);
        Assert.AreEqual(1, service.ActiveConnectionCount);
    }

    [TestMethod]
    public async Task Dispose_DisposesEveryRetainedRuntimeAndRejectsFurtherUse()
    {
        var factory = new FakeMcpClientSessionFactory();
        var service = CreateService(factory);
        var config = CreateConfig();

        await service.GetAIToolsAsync([config]);
        await service.ResetConnectionsAsync();
        await service.GetAIToolsAsync([config]);

        await service.DisposeAsync();

        Assert.IsTrue(factory.Sessions.All(session => session.DisposeCount == 1));
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            () => service.GetAIToolsAsync([config]));
    }

    private static McpService CreateService(IMcpClientSessionFactory factory)
    {
        return new McpService(
            NullLogger<McpService>.Instance,
            new McpToolAuditLogger(NullLogger<McpToolAuditLogger>.Instance),
            new MCPServerConfigService(),
            factory);
    }

    private static MCPServerConfig CreateConfig()
    {
        return new MCPServerConfig
        {
            Id = "server-1",
            Name = "test-server",
            Description = "test",
            TransportType = "stdio",
            Command = "test-command",
            Arguments = "--flag value",
            Category = "search",
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["API_KEY"] = "secret",
                ["REGION"] = "cn"
            }
        };
    }

    private sealed class FakeMcpClientSessionFactory(bool failFirstList = false)
        : IMcpClientSessionFactory
    {
        public int CreateCount { get; private set; }

        public List<FakeMcpClientSession> Sessions { get; } = [];

        public Task<IMcpClientSession> CreateAsync(
            MCPServerConfig config,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CreateCount++;
            var session = new FakeMcpClientSession(failFirstList && CreateCount == 1);
            Sessions.Add(session);
            return Task.FromResult<IMcpClientSession>(session);
        }
    }

    private sealed class FakeMcpClientSession(bool failList) : IMcpClientSession
    {
        private static readonly IReadOnlyList<AITool> Tools =
        [
            AIFunctionFactory.Create(
                (Func<string>)(() => "search"),
                new AIFunctionFactoryOptions { Name = "search" }),
            AIFunctionFactory.Create(
                (Func<string>)(() => "trade"),
                new AIFunctionFactoryOptions { Name = "trade" })
        ];

        public int DisposeCount { get; private set; }

        public Task<IReadOnlyList<AITool>> ListToolsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (failList)
                throw new InvalidOperationException("list failed");

            return Task.FromResult(Tools);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
