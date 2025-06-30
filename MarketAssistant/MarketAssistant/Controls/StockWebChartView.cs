using MarketAssistant.Applications.Stocks.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace MarketAssistant.Controls;

public class StockWebChartView : ContentView
{
    private readonly WebView _webView;
    private bool _isInitialized = false;

    public StockWebChartView()
    {
        _webView = new WebView
        {
            HeightRequest = -1, // 自动填充高度
            WidthRequest = -1, // 自动填充宽度
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };

        // 加载HTML文件
        string htmlPath = "kline_chart.html";
        _webView.Source = new HtmlWebViewSource
        {
            BaseUrl = FileSystem.AppDataDirectory,
            Html = LoadHtmlContent(htmlPath)
        };

        Content = _webView;

        // 监听WebView加载完成事件
        _webView.Navigated += (sender, e) =>
        {
            _isInitialized = true;
        };
    }

    private readonly ILogger<StockWebChartView> _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<StockWebChartView>();

    /// <summary>
    /// 加载HTML内容
    /// </summary>
    private string LoadHtmlContent(string htmlFileName)
    {
        try
        {
            using var stream = FileSystem.OpenAppPackageFileAsync(htmlFileName).Result;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"加载HTML文件失败: {ex.Message}");
            return $"<html><body><h1>加载图表失败</h1><p>{ex.Message}</p></body></html>";
        }
    }

    /// <summary>
    /// 等待WebView初始化完成
    /// </summary>
    private async Task WaitForInitializationAsync()
    {
        // 最多等待5秒钟
        for (int i = 0; i < 50; i++)
        {
            if (_isInitialized)
                return;

            await Task.Delay(100);
        }

        throw new TimeoutException("WebView初始化超时");
    }

    /// <summary>
    /// 设置图表标题
    /// </summary>
    public async Task SetTitleAsync(string title)
    {
        if (string.IsNullOrEmpty(title))
            return;

        try
        {
            await WaitForInitializationAsync();
            string escapedTitle = title.Replace("\"", "\\\"");
            await _webView.EvaluateJavaScriptAsync($"document.title = \"{escapedTitle}\"");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"设置图表标题失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 使用K线数据更新图表
    /// </summary>
    /// <param name="kLineData">K线数据集合</param>
    public async Task UpdateChartAsync(IEnumerable<StockKLineData> kLineData)
    {
        if (kLineData == null || !kLineData.Any())
            return;

        try
        {
            // 等待WebView初始化完成
            await WaitForInitializationAsync();

            // 设置加载状态
            await _webView.EvaluateJavaScriptAsync("window.stockChartInterface.setLoading(true);");

            // 将数据序列化为JSON
            string jsonData = JsonSerializer.Serialize(kLineData);

            // 使用正确的方式传递JSON数据到JavaScript
            string script = $"window.stockChartInterface.loadData({jsonData});";

            await _webView.EvaluateJavaScriptAsync(script);

            //var platfromView = _webView.Handler.PlatformView;
            //if (platfromView != null)
            //{
            //    var property = platfromView.GetType().GetProperty("CoreWebView2", BindingFlags.Instance | BindingFlags.Public);
            //    if (property != null)
            //    {
            //        var coreWebView2 = property.GetValue(platfromView);
            //        if (coreWebView2 != null)
            //        {
            //            var openMethod = coreWebView2.GetType().GetMethod("OpenDevToolsWindow");
            //            if (openMethod != null)
            //            {
            //                dynamic r = openMethod.Invoke(coreWebView2, null);
            //            }
            //        }
            //    }
            //}

            // 设置加载完成状态
            await _webView.EvaluateJavaScriptAsync("window.stockChartInterface.setLoading(false);");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"更新图表失败: {ex.Message}");
            // 显示错误信息
            string errorMessage = ex.Message.Replace("\"", "\\\"");
            await _webView.EvaluateJavaScriptAsync($"window.stockChartInterface.setError(true, \"{errorMessage}\");");
        }
    }
}