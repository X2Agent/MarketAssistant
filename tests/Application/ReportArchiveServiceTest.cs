using MarketAssistant.Services.Archive;
using Microsoft.Extensions.Logging.Abstractions;

namespace TestMarketAssistant.Application;

/// <summary>
/// ReportArchiveService 读取契约：查询失败必须抛出，不得以空结果掩盖。
/// </summary>
[TestClass]
public class ReportArchiveServiceTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetSummaries_WhenCancelled_ShouldThrowInsteadOfReturningEmpty()
    {
        var service = new ReportArchiveService(NullLogger<ReportArchiveService>.Instance);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.GetSummariesAsync("600519", cancellationToken: cts.Token));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LoadReport_WhenCancelled_ShouldThrowInsteadOfReturningNull()
    {
        var service = new ReportArchiveService(NullLogger<ReportArchiveService>.Instance);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.LoadAsync(1, cts.Token));
    }
}
