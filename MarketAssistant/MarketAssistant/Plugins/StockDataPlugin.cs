using MarketAssistant.Applications.Stocks.Models;
using MarketAssistant.Infrastructure;
using MarketAssistant.Plugins.Models;
using Microsoft.Playwright;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;
namespace MarketAssistant.Plugins;

public sealed class StockDataPlugin
{
    //https://cxdata.caixin.com/stock/details/101002165
    //https://learn.microsoft.com/en-us/semantic-kernel/concepts/plugins/?pivots=programming-language-csharp
    //https://medium.com/@nicolas-31/create-plugins-for-semantic-kernel-fdd9e3f24d2e
    readonly HttpClient httpClient;
    readonly IServiceProvider serviceProvider;
    private readonly PlaywrightService _playwrightService;
    private string zhiTuToken = "";

    public StockDataPlugin(IServiceProvider serviceProvider, IUserSettingService userSettingService)
    {
        httpClient = new HttpClient();
        this.serviceProvider = serviceProvider;
        _playwrightService = serviceProvider.GetRequiredService<PlaywrightService>();
        zhiTuToken = userSettingService.CurrentSetting.ZhiTuApiToken;
    }

    private Kernel GetKernel()
    {
        return serviceProvider.GetRequiredService<Kernel>();
    }

    [KernelFunction("get_stock_price"), Description("根据股票代码获取实时价格数据")]
    public async Task<StockPriceInfo> GetStockPriceAsync(string stockSymbol)
    {
        try
        {

            var url = $"https://x-quote.cls.cn/quote/stock/basic?secu_code={stockSymbol}&fields=open_px,av_px,high_px,low_px,change,change_px,down_price,change_3,change_5,qrr,entrust_rate,tr,amp,TotalShares,mc,NetAssetPS,NonRestrictedShares,cmc,business_amount,business_balance,pe,ttm_pe,pb,secu_name,secu_code,trade_status,secu_type,preclose_px,up_price,last_px&app=CailianpressWeb&os=web&sv=8.4.6";

            var response = await httpClient.GetStringAsync(url);
            var jsonDocument = JsonDocument.Parse(response);

            var stockPriceInfo = new StockPriceInfo();

            var data = jsonDocument.RootElement.GetProperty("data");

            stockPriceInfo.CurrentPrice = data.GetProperty("last_px").GetDecimal();
            stockPriceInfo.PriceChange = data.GetProperty("change_px").GetDecimal();
            stockPriceInfo.PercentageChange = data.GetProperty("change").GetDecimal();
            stockPriceInfo.HighPrice = data.GetProperty("high_px").GetDecimal();
            stockPriceInfo.LowPrice = data.GetProperty("low_px").GetDecimal();
            stockPriceInfo.Volume = data.GetProperty("business_amount").GetDecimal() / 10000; // 转换为万手
            stockPriceInfo.Amount = data.GetProperty("business_balance").GetDecimal() / 100000000; // 转换为亿
            stockPriceInfo.TurnoverRate = data.GetProperty("tr").GetDecimal();
            stockPriceInfo.PercentageChange3Day = data.GetProperty("change_3").GetDecimal();
            stockPriceInfo.PercentageChange5Day = data.GetProperty("change_5").GetDecimal();
            stockPriceInfo.TotalShares = data.GetProperty("TotalShares").GetDecimal();
            stockPriceInfo.MarketCapitalization = data.GetProperty("mc").GetDecimal() / 100000000; // 转换为亿

            return stockPriceInfo;
        }
        catch (Exception ex)
        {
            throw new Exception($"处理股票价格数据时发生错误: {ex.Message}", ex);
        }
    }

    [KernelFunction("get_stock_company_info"), Description("获取公司基本面数据")]
    public async Task<StockCompanyInfo> GetStockCompanyInfoAsync(string stockSymbol)
    {
        try
        {

            // 只保留 stockSymbol 中的数字部分
            stockSymbol = new string(stockSymbol.Where(char.IsDigit).ToArray());

            var url = $"https://api.zhituapi.com/hs/gs/gsjj/{stockSymbol}?token={zhiTuToken}";

            var response = await httpClient.GetStringAsync(url);
            var info = JsonSerializer.Deserialize<StockCompanyInfo>(response);

            return info ?? throw new Exception("GetStockCompanyInfoAsync返回数据为空");
        }
        catch (Exception ex)
        {
            throw new Exception($"处理公司基本面数据时发生错误: {ex.Message}", ex);
        }
    }

    [KernelFunction("get_fund_flow"), Description("获取股票资金流数据")]
    public async Task<FundFlow> GetFundFlowAsync(string stockSymbol)
    {
        try
        {

            var url = $"https://x-quote.cls.cn/quote/stock/fundflow?secu_code={stockSymbol}&app=CailianpressWeb&os=web&sv=8.4.6";

            var response = await httpClient.GetStringAsync(url);
            var jsonDocument = JsonDocument.Parse(response);

            var fundFlow = new FundFlow();

            var data = jsonDocument.RootElement.GetProperty("data");

            fundFlow.MainFundIn = data.GetProperty("main_fund_in").GetInt64() / 10000f; // 转换为万元
            fundFlow.MainFundOut = data.GetProperty("main_fund_out").GetInt64() / 10000f; // 转换为万元
            fundFlow.MainFundDiff = data.GetProperty("main_fund_diff").GetInt64() / 10000f; // 转换为万元
            fundFlow.SuperFundDiff = data.GetProperty("super_fund_diff").GetInt64() / 10000f; // 转换为万元
            fundFlow.LargeFundDiff = data.GetProperty("large_fund_diff").GetInt64() / 10000f; // 转换为万元
            fundFlow.MediumFundDiff = data.GetProperty("medium_fund_diff").GetInt64() / 10000f; // 转换为万元
            fundFlow.LittleFundDiff = data.GetProperty("little_fund_diff").GetInt64() / 10000f; // 转换为万元
            fundFlow.MainFund3 = data.GetProperty("main_fund_3").GetInt64() / 10000f; // 转换为万元
            fundFlow.MainFund5 = data.GetProperty("main_fund_5").GetInt64() / 10000f; // 转换为万元
            fundFlow.MainFund10 = data.GetProperty("main_fund_10").GetInt64() / 10000f; // 转换为万元
            fundFlow.MainFund20 = data.GetProperty("main_fund_20").GetInt64() / 10000f; // 转换为万元
            fundFlow.Date = data.GetProperty("date").GetInt32(); // 解析日期

            return fundFlow;
        }
        catch (Exception ex)
        {
            throw new Exception($"处理资金流数据时发生错误: {ex.Message}", ex);
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

    [KernelFunction("get_financial_data"), Description("获取上市公司近四个季度的主要财务指标")]
    public async Task<List<StockFinancialData>> GetFinancialDataAsync(string stockSymbol)
    {
        try
        {

            // 只保留 stockSymbol 中的数字部分
            string stockCode = new string(stockSymbol.Where(char.IsDigit).ToArray());

            // 获取最新财报数据
            var url = $"https://api.zhituapi.com/hs/gs/cwzb/{stockCode}?token={zhiTuToken}";

            var response = await httpClient.GetStringAsync(url);
            var financialDatas = JsonSerializer.Deserialize<List<StockFinancialData>>(response);

            return financialDatas ?? new();
        }
        catch (Exception ex)
        {
            throw new Exception($"获取上市公司近四个季度的主要财务指标时发生错误: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 获取财联社股票代码格式
    /// </summary>
    private string GetStockCodeWithPrefix(string stockCode)
    {
        if (stockCode.Length != 6)
        {
            return stockCode;
        }

        // 上海证券交易所股票代码前缀为SH，深圳证券交易所股票代码前缀为SZ
        if (stockCode.StartsWith("6"))
        {
            return $"SH{stockCode}";
        }
        else if (stockCode.StartsWith("0") || stockCode.StartsWith("3"))
        {
            return $"SZ{stockCode}";
        }

        return stockCode;
    }

    [KernelFunction("get_quarterly_profit"), Description("获取上市公司近一年各季度的利润数据")]
    public async Task<List<QuarterlyProfit>> GetQuarterlyProfitAsync(string stockSymbol)
    {
        try
        {
            // 只保留 stockSymbol 中的数字部分
            string stockCode = new string(stockSymbol.Where(char.IsDigit).ToArray());

            // 获取季度利润数据
            var url = $"https://api.zhituapi.com/hs/gs/jdlr/{stockCode}?token={zhiTuToken}";

            var response = await httpClient.GetStringAsync(url);
            var quarterlyProfits = JsonSerializer.Deserialize<List<QuarterlyProfit>>(response);

            return quarterlyProfits ?? new List<QuarterlyProfit>();
        }
        catch (Exception ex)
        {
            throw new Exception($"处理季度利润数据时发生错误: {ex.Message}", ex);
        }
    }

    [KernelFunction("get_quarterly_cash_flow"), Description("获取上市公司近一年各季度的现金流数据")]
    public async Task<List<QuarterlyCashFlow>> GetQuarterlyCashFlowAsync(string stockSymbol)
    {
        try
        {
            // 只保留 stockSymbol 中的数字部分
            string stockCode = new string(stockSymbol.Where(char.IsDigit).ToArray());

            // 获取季度现金流数据
            var url = $"https://api.zhituapi.com/hs/gs/jdxj/{stockCode}?token={zhiTuToken}";

            var response = await httpClient.GetStringAsync(url);
            var quarterlyCashFlows = JsonSerializer.Deserialize<List<QuarterlyCashFlow>>(response);

            return quarterlyCashFlows ?? new List<QuarterlyCashFlow>();
        }
        catch (Exception ex)
        {
            throw new Exception($"处理季度现金流数据时发生错误: {ex.Message}", ex);
        }
    }

    [KernelFunction("get_news_list")]
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

                var newsElements = await page.QuerySelectorAllAsync(".telegraph-list");

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
                            Url = link,
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
