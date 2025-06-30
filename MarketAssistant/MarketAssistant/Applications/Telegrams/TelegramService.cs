using MarketAssistant.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace MarketAssistant.Applications.Telegrams;

public class TelegramService
{
    private readonly ILogger<TelegramService> _logger;
    private readonly PlaywrightService _playwrightService;

    public TelegramService(ILogger<TelegramService> logger, PlaywrightService playwrightService)
    {
        _logger = logger;
        _playwrightService = playwrightService;
    }

    /// <summary>
    /// 从同花顺网站获取实时新闻数据
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>实时新闻列表</returns>
    public async Task<List<Telegram>> GetTelegraphsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var url = "https://news.10jqka.com.cn/realtimenews.html";

            return await _playwrightService.ExecuteWithPageAsync(async page =>
            {
                var telegraphs = new List<Telegram>();

                await page.GotoAsync(url);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

                // 获取快讯列表
                var newsElements = await page.QuerySelectorAllAsync("ul.newsText.all > li");

                foreach (var newsElement in newsElements)
                {
                    try
                    {
                        // 获取时间
                        var timeElement = await newsElement.QuerySelectorAsync(".newsTimer");
                        var time = timeElement != null ? await timeElement.InnerTextAsync() : "";

                        // 获取标题和内容
                        var detailElement = await newsElement.QuerySelectorAsync(".newsDetail a");
                        if (detailElement == null) continue;

                        // 判断是否为重要快讯
                        var isImportant = await detailElement.EvaluateAsync<bool>("el => el.classList.contains('red')");

                        var titleElement = await detailElement.QuerySelectorAsync("strong");
                        var title = titleElement != null ? await titleElement.InnerTextAsync() : "";

                        // 获取链接
                        var link = await detailElement.GetAttributeAsync("href") ?? "";

                        // 获取内容（标题后面的文本）
                        var fullText = await detailElement.InnerTextAsync();
                        var content = fullText.Replace(title, "").Trim();

                        // 获取相关股票列表
                        var stockLinks = await newsElement.QuerySelectorAllAsync(".newsLink a");
                        var stocks = new List<string>();
                        foreach (var stockLink in stockLinks)
                        {
                            var stockText = await stockLink.InnerTextAsync();
                            stocks.Add(stockText.Trim());
                        }

                        telegraphs.Add(new Telegram
                        {
                            Time = time,
                            Title = title,
                            Content = content,
                            Url = link,
                            Stocks = stocks,
                            IsImportant = isImportant
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"解析新闻元素时出错: {ex.Message}");
                    }
                }

                return telegraphs;
            }, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"GetRealtimeNewsAsync 发生异常: {ex.Message}");
            throw;
        }
    }
}
