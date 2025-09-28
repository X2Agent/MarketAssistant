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
        // 优先使用用户 Kernel 服务，失败时再尝试直接解析（兼容旧逻辑）
        var userSvc = serviceProvider.GetService<IKernelFactory>();
        if (userSvc != null)
        {
            if (userSvc.TryCreateKernel(out var k, out var errMsg))
                return k;
            if (!string.IsNullOrEmpty(errMsg))
                throw new InvalidOperationException($"Kernel 未就绪: {errMsg}");
        }
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

    /// <summary>
    /// 根据新闻Url获取新闻详情
    /// </summary>
    /// <param name="url"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private async Task<string> GetNewsContentAsync(string url)
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

    /// <summary>
    /// 获取指定股票新闻列表（标题/来源/链接）
    /// </summary>
    /// <param name="stockSymbol"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private async Task<IEnumerable<NewsItem>> GetNewsListAsync(string stockSymbol)
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

    [KernelFunction("get_stock_news_context"), Description("获取指定股票的聚合新闻上下文，一次返回最近且相关的新闻要点。默认返回精简要点，可通过 response_format 控制详细程度。")]
    [return: Description("返回高信号的新闻上下文条目列表（Title/Source/Url/Summary）。concise 模式仅返回必要要点；detailed 模式包含更长摘要片段。")]
    public async Task<IEnumerable<NewsItem>> GetStockNewsContextAsync(
        [Description("股票代码，支持含前缀或仅数字")] string stockSymbol,
        [Description("返回的新闻条数上限，默认 5，建议 1-10")] int topK = 5,
        [Description("响应格式：concise | detailed。默认 concise 更省 token")] string responseFormat = "concise")
    {
        try
        {
            topK = Math.Clamp(topK, 1, 10);

            var list = await GetNewsListAsync(stockSymbol);
            var results = list.Take(topK).ToList();

            bool isDetailed = string.Equals(responseFormat, "detailed", StringComparison.OrdinalIgnoreCase);

            if (isDetailed)
            {
                foreach (var item in results)
                {
                    try
                    {
                        var content = await GetNewsContentAsync(item.Url);

                        // 简单截断作为摘要片段，避免长文本占用上下文
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            var normalized = content.Trim();
                            item.Summary = normalized.Length <= 500 ? normalized : normalized.Substring(0, 500);
                        }
                    }
                    catch
                    {
                        // 单条失败不影响整体，保持 Summary 为空
                    }
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            // 以可读错误提示帮助代理调整调用策略
            throw new Exception($"获取聚合新闻上下文失败: {ex.Message}. 可尝试：降低 topK、将 response_format 设为 'concise' 或缩小时间范围。", ex);
        }
    }
}
