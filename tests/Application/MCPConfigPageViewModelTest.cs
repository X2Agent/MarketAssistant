using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Mcp;
using MarketAssistant.Services.Notification;
using MarketAssistant.Services.Settings;
using MarketAssistant.ViewModels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TestMarketAssistant.Infrastructure;

namespace TestMarketAssistant.Application;

[TestClass]
public sealed class MCPConfigPageViewModelTest
{
    [TestMethod]
    [TestCategory("Unit")]
    [DataRow(1, false, McpTestStatusKind.Success, DisplayName = "Tools found")]
    [DataRow(0, false, McpTestStatusKind.Warning, DisplayName = "No tools")]
    [DataRow(0, true, McpTestStatusKind.Failed, DisplayName = "Connection failure")]
    public async Task TestConnection_Result_ShouldExposeExplicitStatusKind(
        int toolCount, bool fails, McpTestStatusKind expected)
    {
        var (viewModel, mcpService) = CreateViewModel();
        var setup = mcpService.Setup(s => s.GetAIToolsAsync(
            It.IsAny<IEnumerable<MCPServerConfig>>(), It.IsAny<CancellationToken>()));
        if (fails)
        {
            setup.ThrowsAsync(new InvalidOperationException("connection failed"));
        }
        else
        {
            setup.ReturnsAsync(Enumerable.Range(0, toolCount).Select(_ => Mock.Of<AITool>()).ToList());
        }
        viewModel.AddServerCommand.Execute(null);
        viewModel.Name = "Test server";
        viewModel.Command = "npx";

        await viewModel.TestConnectionCommand.ExecuteAsync(null);

        Assert.AreEqual(expected, viewModel.TestStatusKind);
        Assert.IsFalse(viewModel.IsTesting);
        Assert.IsFalse(string.IsNullOrWhiteSpace(viewModel.TestStatus));
        Assert.AreEqual(expected == McpTestStatusKind.Success, viewModel.IsTestStatusSuccessful);
        Assert.AreEqual(expected == McpTestStatusKind.Warning, viewModel.IsTestStatusWarning);
        Assert.AreEqual(expected == McpTestStatusKind.Failed, viewModel.IsTestStatusFailed);
    }

    private static (MCPConfigPageViewModel ViewModel, Mock<McpService> McpService) CreateViewModel()
    {
        var configPath = Path.Combine(Path.GetTempPath(), $"market-assistant-mcp-{Guid.NewGuid():N}.json");
        var configService = new MCPServerConfigService(configPath, new InMemorySecureSettingsStore());
        var auditLogger = new McpToolAuditLogger(NullLogger<McpToolAuditLogger>.Instance);
        var mcpService = new Mock<McpService>(
            NullLogger<McpService>.Instance, auditLogger, configService);
        var viewModel = new MCPConfigPageViewModel(
            configService,
            Mock.Of<INotificationService>(),
            Mock.Of<IDialogService>(),
            mcpService.Object,
            null!,
            auditLogger,
            NullLogger<MCPConfigPageViewModel>.Instance);
        return (viewModel, mcpService);
    }
}
