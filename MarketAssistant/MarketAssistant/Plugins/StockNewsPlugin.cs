using MarketAssistant.Infrastructure;
using MarketAssistant.Plugins.Models;
using Microsoft.Playwright;
using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace MarketAssistant.Plugins;

public class StockNewsPlugin
{
    readonly IServiceProvider serviceProvider;
    private readonly PlaywrightService _playwrightService;

    public StockNewsPlugin(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
        _playwrightService = serviceProvider.GetRequiredService<PlaywrightService>();
    }

    private Kernel GetKernel()
    {
        return serviceProvider.GetRequiredService<Kernel>();
    }

    /// <summary>
    /// 获取财联社股票代码格式（如 SH600000、SZ000001）
    /// </summary>
    private string GetStockCodeWithPrefix(string stockCode)
    {
        // 提取所有数字字符
        string digits = new string(stockCode.Where(char.IsDigit).ToArray());

        string prefix = GetExchangePrefix(digits);
        return $"{prefix}{digits}";

        static string GetExchangePrefix(string digits)
        {
            // 沪市逻辑：60开头、688开头（科创板）、900开头（B股）
            if (digits.StartsWith("60") ||
                digits.StartsWith("688") ||
                digits.StartsWith("900"))
                return "SH";

            // 深市逻辑（其他所有情况）：00开头、300开头（创业板）、200开头（B股）
            return "SZ";
        }
    }

    [KernelFunction("get_news_content"), Description("根据新闻Url获取新闻详情")]
    public async Task<string> GetNewsContentAsync(string url)
    {
        try
        {
            var sr = new SmartReader.Reader(url);
            sr.Debug = false;

            var article = sr.GetArticle();
            if (article.IsReadable)
            {
                return article.TextContent;
            }

            // 使用YAML插件进行内容识别
            string promptYaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Plugins", "Yaml", "extract_article_content.yaml"));
            var kernel = GetKernel();
            KernelFunction extractContentFunc = kernel.CreateFunctionFromPromptYaml(promptYaml);
            var result = await extractContentFunc.InvokeAsync(kernel, new() { ["html_content"] = article.Content });
            return result.GetValue<string>() ?? "";
        }
        catch (Exception ex)
        {
            throw new Exception($"处理新闻内容时发生错误: {ex.Message}", ex);
        }
    }

    [KernelFunction("get_news_list"), Description("获取指定股票新闻列表")]
    [return: Description("返回一个元组集合，每个元组包含新闻标题(string)和对应的URL链接(string)，用于分析股票相关的最新新闻动态")]
    public async Task<IEnumerable<NewsItem>> GetNewsListAsync(string stockSymbol)
    {
        try
        {
            stockSymbol = GetStockCodeWithPrefix(stockSymbol).ToLower();

            var url = $"https://www.cls.cn/stock?code={stockSymbol}";

            // 使用PlaywrightService获取Browser实例
            return await _playwrightService.ExecuteWithPageAsync(async page =>
            {
                await page.GotoAsync(url);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

                var newsElements = await page.QuerySelectorAllAsync("div.p-t-20.p-b-20.b-b-w-1.b-b-s-s.b-c-e6e7ea");

                var newsList = new List<NewsItem>();

                foreach (var newsElement in newsElements)
                {
                    var titleElement = await newsElement.QuerySelectorAsync("a.c-222.line3");
                    if (titleElement != null)
                    {
                        var title = await titleElement.InnerTextAsync();
                        var link = await titleElement.GetAttributeAsync("href");
                        if (!string.IsNullOrEmpty(link) && !link.StartsWith("http"))
                        {
                            link = $"https://www.cls.cn{link}";
                        }

                        // 获取新闻来源
                        string source = "";
                        var sourceElement = await newsElement.QuerySelectorAsync("div.f-r");
                        if (sourceElement != null)
                        {
                            source = await sourceElement.InnerTextAsync();
                        }

                        newsList.Add(new NewsItem()
                        {
                            Title = title,
                            Url = link ?? "",
                            Source = source
                        });
                    }
                }

                return newsList;
            });
        }
        catch (Exception ex)
        {
            throw new Exception($"处理新闻列表时发生错误: {ex.Message}", ex);
        }
    }
}
