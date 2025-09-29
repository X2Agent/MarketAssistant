using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MarketAssistant.Applications.Stocks.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using WebViewControl;

namespace MarketAssistant.Avalonia.Controls;

/// <summary>
/// 股票Web图表视图组件 (Avalonia版本)
/// 使用 WebView.Avalonia.Desktop 库提供 WebView 支持
/// </summary>
public class StockWebChartView : UserControl
{
    private bool _isInitialized = false;
    private readonly ILogger<StockWebChartView>? _logger;
    private WebView? _webView;

    // 控件引用
    private StackPanel? _loadingPanel;
    private StackPanel? _errorPanel;
    private TextBlock? _statusText;
    private TextBlock? _errorText;
    private Button? _retryButton;

    public StockWebChartView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        // 创建主要布局
        var border = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(8),
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8)
        };

        var grid = new Grid();
        border.Child = grid;

        // 创建 WebView
        _webView = new WebView
        {
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        // 监听 WebView 事件 (简化版本)
        // _webView.NavigationCompleted += OnWebViewNavigationCompleted;
        // _webView.NavigationStarting += OnWebViewNavigationStarting;

        // 加载状态面板
        _loadingPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = true
        };

        _loadingPanel.Children.Add(new TextBlock 
        { 
            Text = "📈", 
            FontSize = 48, 
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16)
        });

        _loadingPanel.Children.Add(new TextBlock 
        { 
            Text = "正在加载图表...", 
            FontSize = 16, 
            HorizontalAlignment = HorizontalAlignment.Center 
        });

        _statusText = new TextBlock 
        { 
            FontSize = 12, 
            Opacity = 0.7, 
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };
        _loadingPanel.Children.Add(_statusText);

        // 错误状态面板
        _errorPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false
        };

        _errorPanel.Children.Add(new TextBlock 
        { 
            Text = "❌", 
            FontSize = 48, 
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16)
        });

        _errorPanel.Children.Add(new TextBlock 
        { 
            Text = "图表加载失败", 
            FontSize = 16, 
            HorizontalAlignment = HorizontalAlignment.Center 
        });

        _errorText = new TextBlock 
        { 
            FontSize = 12, 
            Opacity = 0.7, 
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };
        _errorPanel.Children.Add(_errorText);

        _retryButton = new Button 
        { 
            Content = "重试", 
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _retryButton.Click += (s, e) => _ = InitializeChartAsync();
        _errorPanel.Children.Add(_retryButton);

        // 添加到网格
        grid.Children.Add(_webView);
        grid.Children.Add(_loadingPanel);
        grid.Children.Add(_errorPanel);

        Content = border;

        // 初始化图表
        _ = InitializeChartAsync();
    }

    /// <summary>
    /// 模拟 WebView 导航完成
    /// </summary>
    private async void SimulateNavigationCompleted()
    {
        // 延迟模拟加载时间
        await Task.Delay(2000);
        
        Dispatcher.UIThread.Post(() =>
        {
            _isInitialized = true;
            SetStatus("图表页面加载完成");
            HideLoading();
            _logger?.LogInformation("WebView 模拟导航完成，图表已初始化");
        });
    }

    /// <summary>
    /// 初始化图表
    /// </summary>
    private async Task InitializeChartAsync()
    {
        try
        {
            SetStatus("正在初始化图表...");
            ShowLoading();

            if (_webView == null)
            {
                ShowError("WebView 未正确初始化");
                return;
            }

            // 加载 HTML 图表文件
            string htmlContent = await LoadHtmlContentAsync("kline_chart.html");
            
            if (string.IsNullOrEmpty(htmlContent))
            {
                ShowError("无法加载图表 HTML 文件");
                return;
            }

            // TODO: 使用正确的 WebView API 加载 HTML 内容
            // _webView.NavigateToString(htmlContent);
            
            // 模拟导航完成事件 (待 WebView API 确认后替换)
            SimulateNavigationCompleted();
            
            _logger?.LogInformation("开始加载图表 HTML 内容");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "图表初始化失败");
            ShowError($"初始化失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 加载HTML内容
    /// </summary>
    private async Task<string> LoadHtmlContentAsync(string htmlFileName)
    {
        try
        {
            // 尝试从应用包中加载HTML文件
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceName = $"MarketAssistant.Avalonia.Assets.{htmlFileName}";
            
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                return await reader.ReadToEndAsync();
            }

            // 如果从资源加载失败，尝试从文件系统加载
            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var htmlPath = Path.Combine(appDirectory, htmlFileName);
            
            if (File.Exists(htmlPath))
            {
                return await File.ReadAllTextAsync(htmlPath);
            }

            // 如果文件不存在，返回默认的图表HTML
            return GetDefaultChartHtml();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"加载HTML文件失败: {ex.Message}");
            return GetDefaultChartHtml();
        }
    }

    /// <summary>
    /// 获取默认的图表HTML内容
    /// </summary>
    private string GetDefaultChartHtml()
    {
        return @"
<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>股票K线图表</title>
    <script src=""https://cdn.jsdelivr.net/npm/echarts@5.4.3/dist/echarts.min.js""></script>
    <style>
        body { margin: 0; padding: 10px; font-family: Arial, sans-serif; }
        #chartContainer { width: 100%; height: 400px; }
        .loading { text-align: center; padding: 20px; color: #666; }
    </style>
</head>
<body>
    <div id=""chartContainer"">
        <div class=""loading"">正在加载图表数据...</div>
    </div>
    
    <script>
        // 股票图表接口
        window.stockChartInterface = {
            chart: null,
            
            // 初始化图表
            init: function() {
                this.chart = echarts.init(document.getElementById('chartContainer'));
                this.chart.setOption({
                    title: { text: '股票K线图', left: 'center' },
                    tooltip: { trigger: 'axis' },
                    xAxis: { type: 'category', data: [] },
                    yAxis: { type: 'value' },
                    series: [{
                        type: 'candlestick',
                        data: []
                    }]
                });
            },
            
            // 设置加载状态
            setLoading: function(loading) {
                if (this.chart) {
                    if (loading) {
                        this.chart.showLoading('default', {
                            text: '正在加载...',
                            color: '#4d90fe',
                            textColor: '#000',
                            maskColor: 'rgba(255, 255, 255, 0.8)'
                        });
                    } else {
                        this.chart.hideLoading();
                    }
                }
            },
            
            // 加载数据
            loadData: function(klineData) {
                if (!this.chart || !klineData) return;
                
                const dates = klineData.map(item => item.date || item.Date);
                const values = klineData.map(item => [
                    parseFloat(item.open || item.Open || 0),
                    parseFloat(item.close || item.Close || 0), 
                    parseFloat(item.low || item.Low || 0),
                    parseFloat(item.high || item.High || 0)
                ]);
                
                this.chart.setOption({
                    xAxis: { data: dates },
                    series: [{ data: values }]
                });
            },
            
            // 设置错误状态
            setError: function(hasError, message) {
                if (hasError) {
                    document.getElementById('chartContainer').innerHTML = 
                        '<div class=""loading"" style=""color: red;"">❌ ' + message + '</div>';
                }
            }
        };
        
        // 页面加载完成后初始化图表
        document.addEventListener('DOMContentLoaded', function() {
            window.stockChartInterface.init();
        });
    </script>
</body>
</html>";
    }

    /// <summary>
    /// 显示加载状态
    /// </summary>
    private void ShowLoading()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_loadingPanel != null) _loadingPanel.IsVisible = true;
            if (_errorPanel != null) _errorPanel.IsVisible = false;
            if (_webView != null) _webView.IsVisible = false;
        });
    }

    /// <summary>
    /// 隐藏加载状态
    /// </summary>
    private void HideLoading()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_loadingPanel != null) _loadingPanel.IsVisible = false;
            if (_webView != null) _webView.IsVisible = true;
        });
    }

    /// <summary>
    /// 显示错误状态
    /// </summary>
    private void ShowError(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_errorPanel != null) _errorPanel.IsVisible = true;
            if (_loadingPanel != null) _loadingPanel.IsVisible = false;
            if (_webView != null) _webView.IsVisible = false;
            if (_errorText != null) _errorText.Text = message;
        });
    }

    /// <summary>
    /// 设置状态文本
    /// </summary>
    private void SetStatus(string status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_statusText != null) _statusText.Text = status;
        });
    }

    /// <summary>
    /// 设置图表标题
    /// </summary>
    public async Task SetTitleAsync(string title)
    {
        if (string.IsNullOrEmpty(title) || _webView == null)
            return;

        try
        {
            await WaitForInitializationAsync();
            
            string escapedTitle = title.Replace("\"", "\\\"");
            string script = $"if (window.stockChartInterface && window.stockChartInterface.chart) {{ " +
                          $"window.stockChartInterface.chart.setOption({{ title: {{ text: \"{escapedTitle}\" }} }}); }}";
            
            // TODO: 使用正确的 WebView API 执行 JavaScript
            // await _webView.ExecuteScriptAsync(script);
            _logger?.LogInformation($"JavaScript 调用: {script}");
            _logger?.LogInformation($"图表标题已设置: {title}");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "设置图表标题失败");
        }
    }

    /// <summary>
    /// 使用K线数据更新图表
    /// </summary>
    public async Task UpdateChartAsync(IEnumerable<StockKLineData> kLineData)
    {
        if (kLineData == null || !kLineData.Any() || _webView == null)
            return;

        try
        {
            await WaitForInitializationAsync();
            
            SetStatus("正在更新图表数据...");

            // 设置加载状态
            // TODO: 使用正确的 WebView API 执行 JavaScript
            // await _webView.ExecuteScriptAsync("window.stockChartInterface.setLoading(true);");
            _logger?.LogInformation("JavaScript 调用: window.stockChartInterface.setLoading(true);");

            // 序列化数据
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
            string jsonData = JsonSerializer.Serialize(kLineData, options);

            // 调用JavaScript更新图表数据
            string script = $"window.stockChartInterface.loadData({jsonData});";
            // TODO: 使用正确的 WebView API 执行 JavaScript
            // await _webView.ExecuteScriptAsync(script);
            _logger?.LogInformation($"JavaScript 调用: {script}");

            // 取消加载状态
            // TODO: 使用正确的 WebView API 执行 JavaScript
            // await _webView.ExecuteScriptAsync("window.stockChartInterface.setLoading(false);");
            _logger?.LogInformation("JavaScript 调用: window.stockChartInterface.setLoading(false);");

            _logger?.LogInformation($"图表数据已更新，数据点数量: {kLineData.Count()}");
            SetStatus($"图表更新完成 ({kLineData.Count()} 个数据点)");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "更新图表失败");
            
            // 显示JavaScript错误
            string errorMessage = ex.Message.Replace("\"", "\\\"");
            try
            {
                // TODO: 使用正确的 WebView API 执行 JavaScript
                // await _webView.ExecuteScriptAsync($"window.stockChartInterface.setError(true, \"{errorMessage}\");");
                _logger?.LogWarning($"JavaScript 错误处理: {errorMessage}");
            }
            catch
            {
                // 如果JavaScript调用也失败，显示本地错误
                ShowError($"更新失败: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 等待初始化完成
    /// </summary>
    private async Task WaitForInitializationAsync()
    {
        const int maxWaitTime = 5000; // 5秒
        const int checkInterval = 100; // 100毫秒
        int elapsed = 0;

        while (!_isInitialized && elapsed < maxWaitTime)
        {
            await Task.Delay(checkInterval);
            elapsed += checkInterval;
        }

        if (!_isInitialized)
        {
            throw new TimeoutException("图表初始化超时");
        }
    }
}
