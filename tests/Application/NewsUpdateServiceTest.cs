using System.Collections.Concurrent;
using MarketAssistant.Applications.News;
using MarketAssistant.Applications.Telegrams;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Application;

/// <summary>
/// NewsUpdateService 轮询循环存活性：单次拉取失败（含 HttpClient 超时）只影响本轮，
/// 循环必须继续运行，而不是被当成正常停止而静默退出。
/// </summary>
[TestClass]
[TestCategory("Unit")]
public class NewsUpdateServiceTest
{
    [TestMethod]
    public async Task StartUpdates_FetchTimesOut_ReportsFailureAndKeepsLoopRunning()
    {
        // HttpClient 超时抛出 TaskCanceledException（OperationCanceledException 子类），但循环令牌并未取消
        var telegramService = new Mock<ITelegramService>();
        telegramService.Setup(s => s.GetTelegraphsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("模拟 HttpClient 超时"));
        using var service = new NewsUpdateService(telegramService.Object, NullLogger<NewsUpdateService>.Instance);
        var messages = new ConcurrentQueue<string>();
        service.CountdownUpdated += (_, message) => messages.Enqueue(message);

        service.StartUpdates();
        // 失败后循环先退避 3 秒再进入倒计时：出现倒计时文案即证明循环仍存活
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && !messages.Any(message => message.EndsWith("秒后更新", StringComparison.Ordinal)))
        {
            await Task.Delay(50);
        }

        CollectionAssert.Contains(messages.ToList(), "更新失败");
        Assert.IsTrue(messages.Any(message => message.EndsWith("秒后更新", StringComparison.Ordinal)), "超时后循环应继续进入倒计时");
        Assert.IsTrue(service.IsRunning);
    }
}
