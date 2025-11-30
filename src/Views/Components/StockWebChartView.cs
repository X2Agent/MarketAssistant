using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform;
using MarketAssistant.Applications.Stocks.Models;
using System.Text.Json;
using WebViewControl;

namespace MarketAssistant.Views.Components;

/// <summary>
/// 股票Web图表视图组件 (Avalonia版本)
/// 使用 WebView.Avalonia.Desktop 库提供 WebView 支持
/// </summary>
public class StockWebChartView : UserControl
{
    private bool _isInitialized = false;
    private WebView? _webView;
    private StackPanel? _loadingPanel;
    private StackPanel? _errorPanel;
    private TextBlock? _errorText;
    private Button? _retryButton;

    // 定义依赖属性，支持 MVVM 绑定
    public static readonly StyledProperty<IEnumerable<StockKLineData>?> DataProperty =
        AvaloniaProperty.Register<StockWebChartView, IEnumerable<StockKLineData>?>(nameof(Data));

    /// <summary>
    /// K线数据源
    /// </summary>
    public IEnumerable<StockKLineData>? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public StockWebChartView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 监听属性变化
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DataProperty)
        {
            // 当数据源发生变化时，自动更新图表
            _ = UpdateChartAsync(change.NewValue as IEnumerable<StockKLineData>);
        }
    }

    private void InitializeComponent()
    {
        var grid = new Grid();

        // 创建 WebView
        _webView = new WebView
        {
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        // 加载状态面板
        _loadingPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = true
        };
        _loadingPanel.Children.Add(new TextBlock
        {
            Text = "正在加载图表...",
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        // 错误状态面板
        _errorPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
            Spacing = 12
        };
        _errorPanel.Children.Add(new TextBlock
        {
            Text = "图表加载失败",
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        _errorText = new TextBlock
        {
            FontSize = 12,
            Opacity = 0.7,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _errorPanel.Children.Add(_errorText);

        _retryButton = new Button
        {
            Content = "重试",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _retryButton.Click += (s, e) => _ = InitializeChartAsync();
        _errorPanel.Children.Add(_retryButton);

        // 添加到网格
        grid.Children.Add(_webView);
        grid.Children.Add(_loadingPanel);
        grid.Children.Add(_errorPanel);

        Content = grid;

        // 延迟初始化：不在构造时立即初始化，等待数据加载完成后再初始化
        // 这样可以让页面更快地打开
    }

    /// <summary>
    /// WebView 导航完成事件处理器
    /// </summary>
    private void OnWebViewNavigated(string url, string? title)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _isInitialized = true;
            HideLoading();
        });
    }

    /// <summary>
    /// 初始化图表
    /// </summary>
    private async Task InitializeChartAsync()
    {
        try
        {
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

            // 监听 WebView 加载完成事件（必须在 LoadHtml 之前注册）
            _webView.Navigated += OnWebViewNavigated;

            // 使用 WebView 的 LoadHtml 方法加载 HTML 内容
            _webView.LoadHtml(htmlContent);
        }
        catch (Exception ex)
        {
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
            // 方法1：使用 Avalonia 原生的 AssetLoader（推荐）
            // 资源 URI 格式：avares://AssemblyName/Path/To/File
            var assetUri = new Uri($"avares://MarketAssistant/Assets/Raw/{htmlFileName}");

            if (AssetLoader.Exists(assetUri))
            {
                using var stream = AssetLoader.Open(assetUri);
                using var reader = new StreamReader(stream);
                return await reader.ReadToEndAsync();
            }

            // 备用方案 - 从文件系统加载（用于开发调试）
            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var htmlPath = Path.Combine(appDirectory, "Assets", "Raw", htmlFileName);

            if (File.Exists(htmlPath))
            {
                return await File.ReadAllTextAsync(htmlPath);
            }

            // 如果都失败，返回默认HTML
            return GetDefaultChartHtml();
        }
        catch (Exception)
        {
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
    /// 使用K线数据更新图表
    /// </summary>
    public async Task UpdateChartAsync(IEnumerable<StockKLineData> kLineData)
    {
        if (kLineData == null || !kLineData.Any() || _webView == null)
            return;

        try
        {
            // 首次调用时才初始化图表（延迟初始化）
            if (!_isInitialized)
            {
                await InitializeChartAsync();
            }

            await WaitForInitializationAsync();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    // 设置加载状态
                    _webView.ExecuteScript("window.stockChartInterface.setLoading(true);");

                    // 序列化数据
                    var options = new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = false
                    };
                    string jsonData = JsonSerializer.Serialize(kLineData, options);

                    // 调用JavaScript更新图表数据
                    string script = $"window.stockChartInterface.loadData({jsonData});";
                    _webView.ExecuteScript(script);

                    // 取消加载状态
                    _webView.ExecuteScript("window.stockChartInterface.setLoading(false);");
                }
                catch (Exception jsEx)
                {
                    ShowError($"更新图表失败: {jsEx.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            ShowError($"更新失败: {ex.Message}");
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
