using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using MarketAssistant.Applications.Charts.Models;
using MarketAssistant.Views.Controls;
using System.Text.Json;

namespace MarketAssistant.Views.Components;

public class KLineChartView : UserControl
{
    private const int MaxWaitTimeMs = 5000;
    private const int CheckIntervalMs = 100;

    private bool _isInitialized;
    private bool _navigationHandlerSubscribed;
    private bool _isAttachedToVisualTree;
    private CancellationTokenSource? _chartCts;
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

    /// <summary>均线（MA(5/10/20/30/60)）主图叠加是否显示。</summary>
    public static readonly StyledProperty<bool> IsMaVisibleProperty =
        AvaloniaProperty.Register<KLineChartView, bool>(nameof(IsMaVisible), true);

    public bool IsMaVisible
    {
        get => GetValue(IsMaVisibleProperty);
        set => SetValue(IsMaVisibleProperty, value);
    }

    /// <summary>BOLL 主图叠加是否显示。</summary>
    public static readonly StyledProperty<bool> IsBollVisibleProperty =
        AvaloniaProperty.Register<KLineChartView, bool>(nameof(IsBollVisible), true);

    public bool IsBollVisible
    {
        get => GetValue(IsBollVisibleProperty);
        set => SetValue(IsBollVisibleProperty, value);
    }

    /// <summary>成交量（VOL）副图是否显示。</summary>
    public static readonly StyledProperty<bool> IsVolVisibleProperty =
        AvaloniaProperty.Register<KLineChartView, bool>(nameof(IsVolVisible), true);

    public bool IsVolVisible
    {
        get => GetValue(IsVolVisibleProperty);
        set => SetValue(IsVolVisibleProperty, value);
    }

    /// <summary>MACD 副图是否显示。</summary>
    public static readonly StyledProperty<bool> IsMacdVisibleProperty =
        AvaloniaProperty.Register<KLineChartView, bool>(nameof(IsMacdVisible), true);

    public bool IsMacdVisible
    {
        get => GetValue(IsMacdVisibleProperty);
        set => SetValue(IsMacdVisibleProperty, value);
    }

    /// <summary>KDJ 副图是否显示。</summary>
    public static readonly StyledProperty<bool> IsKdjVisibleProperty =
        AvaloniaProperty.Register<KLineChartView, bool>(nameof(IsKdjVisible), true);

    public bool IsKdjVisible
    {
        get => GetValue(IsKdjVisibleProperty);
        set => SetValue(IsKdjVisibleProperty, value);
    }

    /// <summary>左上角行情图例（K 线信息条与各指标图例）是否显示。</summary>
    public static readonly StyledProperty<bool> IsLegendVisibleProperty =
        AvaloniaProperty.Register<KLineChartView, bool>(nameof(IsLegendVisible), true);

    public bool IsLegendVisible
    {
        get => GetValue(IsLegendVisibleProperty);
        set => SetValue(IsLegendVisibleProperty, value);
    }

    // 指标/图例可见性属性集合：变更时统一下发图表脚本（声明须晚于各 Property，避免静态初始化拿到 null）
    private static readonly AvaloniaProperty[] IndicatorVisibilityProperties =
    [
        IsMaVisibleProperty, IsBollVisibleProperty, IsVolVisibleProperty,
        IsMacdVisibleProperty, IsKdjVisibleProperty, IsLegendVisibleProperty,
    ];

    public KLineChartView()
    {
        InitializeComponent();
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e) => _ = ApplyThemeToChartAsync();

    private async Task ApplyThemeToChartAsync()
    {
        if (!_isAttachedToVisualTree || _webView == null || !_isInitialized)
            return;

        var theme = ActualThemeVariant == ThemeVariant.Dark ? "dark" : "light";
        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (_webView != null)
                    await _webView.InvokeScript($"window.stockChartInterface.setTheme('{theme}');");
            });
        }
        catch (Exception ex)
        {
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
        else if (Array.IndexOf(IndicatorVisibilityProperties, change.Property) >= 0)
        {
            // 指标/图例开关变更：已初始化时立即下发，未初始化时由导航完成回调补推
            _ = ApplyIndicatorVisibilityAsync();
        }
    }

    /// <summary>
    /// 把指标/图例可见性一次性推送到图表脚本（JSON 键与脚本内 setIndicatorVisibility 约定一致）。
    /// </summary>
    private async Task ApplyIndicatorVisibilityAsync()
    {
        if (_webView == null || !_isInitialized)
        {
            return;
        }

        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var state = new
                {
                    ma = IsMaVisible,
                    boll = IsBollVisible,
                    vol = IsVolVisible,
                    macd = IsMacdVisible,
                    kdj = IsKdjVisible,
                    legend = IsLegendVisible,
                };
                string json = JsonSerializer.Serialize(state);
                await _webView.InvokeScript($"window.stockChartInterface.setIndicatorVisibility({json});");
            });
        }
        catch (Exception ex)
        {
            // 图表脚本尚未就绪时忽略，导航完成回调会再次同步
            System.Diagnostics.Debug.WriteLine($"同步K线图指标可见性失败: {ex.Message}");
        }
    }

    private void InitializeComponent()
    {
        var grid = new Grid();
        grid[!Grid.BackgroundProperty] = new DynamicResourceExtension("CardBackgroundBrush");
        _rootGrid = grid;

        _loadingPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = true,
            Spacing = 12
        };
        var loadingDots = new LoadingDots
        {
            DotSize = 8,
            DotSpacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var loadingText = new TextBlock
        {
            Text = "正在加载图表...",
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        loadingText[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondaryBrush");
        _loadingPanel.Children.Add(loadingDots);
        _loadingPanel.Children.Add(loadingText);

        _errorPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
            Spacing = 12
        };
        var errorTitle = new TextBlock
        {
            Text = "图表加载失败",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        errorTitle[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("DangerPanelTextBrush");
        _errorPanel.Children.Add(errorTitle);

        _errorText = new TextBlock
        {
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        _errorText[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondaryBrush");
        _errorPanel.Children.Add(_errorText);

        _retryButton = new Button
        {
            Content = "重试",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _retryButton.Classes.Add("btn-secondary");
        _retryButton.Click += (s, e) => _ = InitializeChartAsync();
        _errorPanel.Children.Add(_retryButton);

        grid.Children.Add(_loadingPanel);
        grid.Children.Add(_errorPanel);
        Content = grid;
    }

    private static NativeWebView CreateWebView() => new()
    {
        IsVisible = false,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch
    };

    /// <summary>
    /// 获取 WebView，仅在已挂载到可视树时按需创建（离开可视树时释放，禁止离屏重建）
    /// </summary>
    private NativeWebView? EnsureWebView()
    {
        if (!_isAttachedToVisualTree)
            return null;
        if (_webView != null)
            return _webView;

        var webView = CreateWebView();
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
            // 初始化 layout 会创建全部指标，此处按宿主当前状态纠正显隐
            Dispatcher.UIThread.Post(() => _ = ApplyIndicatorVisibilityAsync());
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
            if (!_isAttachedToVisualTree)
                return;

            ShowLoading();

            string htmlContent = await LoadHtmlContentAsync("kline_chart.html");
            if (string.IsNullOrEmpty(htmlContent))
            {
                ShowError("无法加载图表 HTML 文件");
                return;
            }

            var webView = EnsureWebView();
            if (webView == null)
                return;

            if (!_navigationHandlerSubscribed)
            {
                webView.NavigationCompleted += OnWebViewNavigated;
                _navigationHandlerSubscribed = true;
            }

            webView.NavigateToString(htmlContent);
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
            var html = await LoadRawAssetTextAsync(htmlFileName);
            if (!string.IsNullOrEmpty(html))
            {
                const string bundlePlaceholder = "/* __KLINECHARTS_BUNDLE__ */";
                if (html.Contains(bundlePlaceholder, StringComparison.Ordinal))
                {
                    var bundleScript = await LoadRawAssetTextAsync("klinecharts.min.js");
                    if (!string.IsNullOrEmpty(bundleScript))
                    {
                        html = html.Replace(bundlePlaceholder, bundleScript, StringComparison.Ordinal);
                    }
                }
                return html;
            }

            return GetDefaultChartHtml();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载图表 HTML 内容失败，回退到默认图表: {ex.Message}");
            return GetDefaultChartHtml();
        }
    }

    private static async Task<string?> LoadRawAssetTextAsync(string fileName)
    {
        var assetUri = new Uri($"avares://MarketAssistant/Assets/Raw/{fileName}");
        if (AssetLoader.Exists(assetUri))
        {
            using var stream = AssetLoader.Open(assetUri);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }

        var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var filePath = Path.Combine(appDirectory, "Assets", "Raw", fileName);
        if (File.Exists(filePath))
        {
            return await File.ReadAllTextAsync(filePath);
        }

        return null;
    }

    private string GetDefaultChartHtml() => @"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>K线图表</title>
    <!-- 仓库未内置 ECharts 本地资源，此处回退到 CDN -->
    <script src=""https://cdn.jsdelivr.net/npm/echarts@5.4.3/dist/echarts.min.js""></script>
    <style>
        body { margin: 0; padding: 10px; font-family: Arial, sans-serif; background-color: #0A0E17; color: #E4EAF5; }
        #chartContainer { width: 100%; height: 400px; }
        .loading { text-align: center; padding: 20px; color: #8894A8; }
    </style>
</head>
<body>
    <div id=""chartContainer""><div class=""loading"">正在加载图表数据...</div></div>
    <script>
        window.stockChartInterface = {
            chart: null,
            init: function() {
                this.chart = echarts.init(document.getElementById('chartContainer'));
                this.chart.setOption({
                    title: { text: 'K线图', left: 'center' },
                    tooltip: { trigger: 'axis' },
                    xAxis: { type: 'category', data: [] },
                    yAxis: { type: 'value' },
                    series: [{ type: 'candlestick', data: [] }]
                });
            },
            setLoading: function(loading) {
                if (!this.chart) return;
                if (loading) {
                    this.chart.showLoading('default', { text: '正在加载...', color: '#1976D2', textColor: '#8894A8', maskColor: 'rgba(10, 14, 23, 0.8)' });
                } else {
                    this.chart.hideLoading();
                }
            },
            loadData: function(klineData) {
                if (!this.chart || !klineData) return;
                const dates = klineData.map(item => item.date || item.Date);
                const values = klineData.map(item => [
                    parseFloat(item.open || item.Open || 0),
                    parseFloat(item.close || item.Close || 0),
                    parseFloat(item.low || item.Low || 0),
                    parseFloat(item.high || item.High || 0)
                ]);
                this.chart.setOption({ xAxis: { data: dates }, series: [{ data: values }] });
            },
            setError: function(hasError, message) {
                if (hasError) {
                    document.getElementById('chartContainer').innerHTML = '<div class=""loading"" style=""color: #EF4444;"">加载失败: ' + message + '</div>';
                }
            }
        };
        document.addEventListener('DOMContentLoaded', function() { window.stockChartInterface.init(); });
    </script>
</body>
</html>";

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
        if (!_isAttachedToVisualTree || kLineData == null || !kLineData.Any())
            return;

        if (EnsureWebView() == null)
            return;

        // 数据源变更可能高频触发，信号量不可立即进入说明上一次更新尚未完成，直接跳过本次
        if (!_updateSemaphore.Wait(0))
            return;

        var ct = _chartCts?.Token ?? CancellationToken.None;
        try
        {
            if (!_isInitialized)
            {
                await InitializeChartAsync();
            }

            await WaitForInitializationAsync(ct);

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (!_isAttachedToVisualTree || _webView == null)
                    return;

                try
                {
                    await _webView.InvokeScript("window.stockChartInterface.setLoading(true);");

                    var options = new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = false
                    };
                    string jsonData = JsonSerializer.Serialize(kLineData, options);
                    await _webView.InvokeScript($"window.stockChartInterface.loadData({jsonData});");
                    await _webView.InvokeScript("window.stockChartInterface.setLoading(false);");
                }
                catch (Exception jsEx)
                {
                    ShowError($"更新图表失败: {jsEx.Message}");
                }
            });
        }
        catch (OperationCanceledException)
        {
            // 控件已脱离可视树取消初始化，不展示错误态
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

    private async Task WaitForInitializationAsync(CancellationToken ct)
    {
        int elapsed = 0;
        while (!_isInitialized && elapsed < MaxWaitTimeMs)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(CheckIntervalMs, ct);
            elapsed += CheckIntervalMs;
        }

        if (!_isInitialized)
        {
            throw new TimeoutException("图表初始化超时");
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttachedToVisualTree = true;
        _chartCts = new CancellationTokenSource();
        ActualThemeVariantChanged += OnActualThemeVariantChanged;

        // 若 Data 在挂载前已完成绑定（如 GoBack 重建视图），在挂载时立即补发渲染
        if (!_isInitialized && Data is IEnumerable<KLineData> data && data.Any())
        {
            _ = UpdateChartAsync(data);
        }
    }

    /// <summary>
    /// 释放 WebView 持有的原生资源（与 RichTextBlock.CleanupCurrentViewer 同一约定），
    /// 避免反复进出资产页累积原生句柄；重新挂载后由 OnAttachedToVisualTree + EnsureWebView 懒恢复
    /// </summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttachedToVisualTree = false;
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;

        try { _chartCts?.Cancel(); }
        catch (ObjectDisposedException) { }
        _chartCts = null;

        if (_webView != null)
        {
            if (_navigationHandlerSubscribed)
            {
                _webView.NavigationCompleted -= OnWebViewNavigated;
            }
            _rootGrid?.Children.Remove(_webView);
            if (_webView is IDisposable disposableWebView)
            {
                try { disposableWebView.Dispose(); }
                catch { /* 忽略 Dispose 异常 */ }
            }
            _webView = null;
        }
        _isInitialized = false;
        _navigationHandlerSubscribed = false;

        base.OnDetachedFromVisualTree(e);
    }
}



