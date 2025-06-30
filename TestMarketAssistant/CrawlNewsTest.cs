using MarketAssistant.Applications.Telegrams;
using MarketAssistant.Infrastructure;
using Microsoft.Extensions.Logging;
using Moq;

namespace TestMarketAssistant;

[TestClass]
public class CrawlNewsTest
{
    private TelegramService _telegramService = null!;
    private Mock<ILogger<TelegramService>> _loggerMock = null!;
    private Mock<PlaywrightService> _playwrightServiceMock = null!;

    [TestInitialize]
    public void Initialize()
    {
        _loggerMock = new Mock<ILogger<TelegramService>>();
        _playwrightServiceMock = new Mock<PlaywrightService>();
        _telegramService = new TelegramService(_loggerMock.Object, _playwrightServiceMock.Object);
    }

    [TestMethod]
    public async Task TestGetTelegraphsAsync()
    {
        // 调用获取实时新闻的方法
        var newsList = await _telegramService.GetTelegraphsAsync();

        // 验证结果
        Assert.IsNotNull(newsList, "新闻列表不应为空");
        Assert.IsTrue(newsList.Count > 0, "应至少获取到一条新闻");

        // 输出获取到的新闻信息
        Console.WriteLine($"共获取到 {newsList.Count} 条新闻");
        foreach (var news in newsList.Take(5)) // 只显示前5条
        {
            Console.WriteLine($"时间: {news.Time}");
            Console.WriteLine($"标题: {news.Title}");
            Console.WriteLine($"内容: {news.Content.Substring(0, Math.Min(100, news.Content.Length))}...");
            Console.WriteLine($"链接: {news.Url}");
            Console.WriteLine("-----------------------------------");
        }
    }
}
