using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Styling;
using MarketAssistant.Applications.Charts.Models;
using System.Text.Json;

namespace MarketAssistant.Views.Components;

public class KLineChartView : UserControl
{
    private const int MaxWaitTimeMs = 5000;
    private const int CheckIntervalMs = 100;

    private bool _isInitialized = false;
    private bool _navigationHandlerSubscribed = false;
    private readonly SemaphoreSlim _updateSemaphore = new(1, 1);
    private NativeWebView? _webView;
    private Grid? _rootGrid;
    private StackPanel? _loadingPanel;
    private StackPanel? _errorPanel;
    private TextBlock? _errorText;
    private Button? _retryButton;

    public static readonly StyledProperty<IEnumerable<KLineData>?> DataProperty =
        AvaloniaProperty.Register<KLineChartView, IEnumerable<KLineData>?>(nameof(Data));

    public IEnumerable<KLineData>? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public KLineChartView()
    {
        InitializeComponent();

        ActualThemeVariantChanged += (_, _) => _ = ApplyThemeToChartAsync();
    }

    private async Task ApplyThemeToChartAsync()
    {
        if (_webView == null || !_isInitialized)
        {
            return;
        }

        var theme = ActualThemeVariant == ThemeVariant.Dark ? "dark" : "light";
        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await _webView.InvokeScript($"window.stockChartInterface.setTheme('{theme}');");
            });
        }
        catch (Exception ex)
        {
            // 图表脚本尚未就绪时忽略，导航完成回调会再次同步主题
            System.Diagnostics.Debug.WriteLine($"同步K线图主题失败: {ex.Message}");
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DataProperty)
        {
            // 当数据源发生变化时，自动更新图表（UpdateChartAsync 内部串行化，跳过并发重入）
            if (change.NewValue is IEnumerable<KLineData> data)
            {
                _ = UpdateChartAsync(data);
            }
        }
    }

    private void InitializeComponent()
    {
        var grid = new Grid();
        _rootGrid = grid;

        _webView = CreateWebView();

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

        grid.Children.Add(_webView);
        grid.Children.Add(_loadingPanel);
        grid.Children.Add(_errorPanel);

        Content = grid;

        // 延迟初始化：不在构造时立即初始化，等待数据加载完成后再初始化
        // 这样可以让页面更快地打开
    }

    private static NativeWebView CreateWebView() => new()
    {
        IsVisible = false,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch
    };

    /// <summary>
    /// 获取 WebView，必要时重建（离开可视树时会释放，重新挂载后懒恢复）
    /// </summary>
    private NativeWebView EnsureWebView()
    {
        if (_webView != null)
            return _webView;

        var webView = CreateWebView();
        // 插入到最底层，保持状态面板位于 WebView 之上
        _rootGrid?.Children.Insert(0, webView);
        _webView = webView;
        return webView;
    }

    private void OnWebViewNavigated(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _isInitialized = true;
            ApplyThemeToChart();
            HideLoading();
        });
    }

    /// <summary>
    /// 在 UI 线程上同步图表主题（导航完成后的首次注入）
    /// </summary>
    private void ApplyThemeToChart()
    {
        Dispatcher.UIThread.Post(() => _ = ApplyThemeToChartAsync());
    }

    private async Task InitializeChartAsync()
    {
        try
        {
            ShowLoading();

            EnsureWebView();

            string htmlContent = await LoadHtmlContentAsync("kline_chart.html");

            if (string.IsNullOrEmpty(htmlContent))
            {
                ShowError("无法加载图表 HTML 文件");
                return;
            }

            // 监听 WebView 加载完成事件（必须在 NavigateToString 之前注册；仅订阅一次，避免重试后重复触发）
            if (!_navigationHandlerSubscribed)
            {
                _webView.NavigationCompleted += OnWebViewNavigated;
                _navigationHandlerSubscribed = true;
            }

            _webView.NavigateToString(htmlContent);
        }
        catch (Exception ex)
        {
            ShowError($"初始化失败: {ex.Message}");
        }
    }

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

            return GetDefaultChartHtml();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载图表 HTML 内容失败，回退到默认图表: {ex.Message}");
            return GetDefaultChartHtml();
        }
    }

    private string GetDefaultChartHtml()
    {
        return @"
<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>K线图表</title>
    <!-- 注意：仓库未内置 ECharts 本地资源，此处回退到 CDN。
         离线环境下图表不可用；链接无 SRI 校验且无 CSP 限制，存在供应链风险，待内置本地资源后替换。 -->
    <script src=""https://cdn.jsdelivr.net/npm/echarts@5.4.3/dist/echarts.min.js""></script>
    <style>
        body { margin: 0; padding: 10px; font-family: Arial, sans-serif; background-color: #0A0E17; color: #E4EAF5; }
        #chartContainer { width: 100%; height: 400px; }
        .loading { text-align: center; padding: 20px; color: #8894A8; }
    </style>
</head>
<body>
    <div id=""chartContainer"">
        <div class=""loading"">正在加载图表数据...</div>
    </div>
    
    <script>
        // K线图表接口
        window.stockChartInterface = {
            chart: null,
            
            // 初始化图表
            init: function() {
                this.chart = echarts.init(document.getElementById('chartContainer'));
                this.chart.setOption({
                    title: { text: 'K线图', left: 'center' },
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
                            color: '#1976D2',
                            textColor: '#8894A8',
                            maskColor: 'rgba(10, 14, 23, 0.8)'
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
                        '<div class=""loading"" style=""color: #EF4444;"">加载失败: ' + message + '</div>';
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

    private void ShowLoading()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_loadingPanel != null) _loadingPanel.IsVisible = true;
            if (_errorPanel != null) _errorPanel.IsVisible = false;
            if (_webView != null) _webView.IsVisible = false;
        });
    }

    private void HideLoading()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_loadingPanel != null) _loadingPanel.IsVisible = false;
            if (_webView != null) _webView.IsVisible = true;
        });
    }

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

    public async Task UpdateChartAsync(IEnumerable<KLineData> kLineData)
    {
        if (kLineData == null || !kLineData.Any())
            return;

        EnsureWebView();

        // 数据源变更可能高频触发，信号量不可立即进入说明上一次更新尚未完成，直接跳过本次
        if (!_updateSemaphore.Wait(0))
            return;

        try
        {
            if (!_isInitialized)
            {
                await InitializeChartAsync();
            }

            await WaitForInitializationAsync();

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    await _webView!.InvokeScript("window.stockChartInterface.setLoading(true);");

                    var options = new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = false
                    };
                    string jsonData = JsonSerializer.Serialize(kLineData, options);

                    string script = $"window.stockChartInterface.loadData({jsonData});";
                    await _webView.InvokeScript(script);

                    await _webView.InvokeScript("window.stockChartInterface.setLoading(false);");
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
        finally
        {
            _updateSemaphore.Release();
        }
    }

    private async Task WaitForInitializationAsync()
    {
        int elapsed = 0;

        while (!_isInitialized && elapsed < MaxWaitTimeMs)
        {
            await Task.Delay(CheckIntervalMs);
            elapsed += CheckIntervalMs;
        }

        if (!_isInitialized)
        {
            throw new TimeoutException("图表初始化超时");
        }
    }

    /// <summary>
    /// 释放 WebView 持有的原生资源（与 RichTextBlock.CleanupCurrentViewer 同一约定），
    /// 避免反复进出资产页累积原生句柄；重新挂载后由 EnsureWebView 懒重建
    /// </summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_webView is IDisposable disposableWebView)
        {
            try { disposableWebView.Dispose(); }
            catch { /* 忽略 Dispose 异常 */ }
        }
        _webView = null;
        _isInitialized = false;
        _navigationHandlerSubscribed = false;
    }
}



