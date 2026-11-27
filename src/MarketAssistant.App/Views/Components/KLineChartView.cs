using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using MarketAssistant.Applications.Charts.Models;
using MarketAssistant.Views.Controls;

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
            HorizontalAlignment = HorizontalAlignment.Center
        };
        loadingText[!TextBlock.FontSizeProperty] = new DynamicResourceExtension("BodySmFontSize");
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
            HorizontalAlignment = HorizontalAlignment.Center
        };
        errorTitle[!TextBlock.FontSizeProperty] = new DynamicResourceExtension("BodySmFontSize");
        errorTitle[!TextBlock.FontWeightProperty] = new DynamicResourceExtension("WeightTitle");
        errorTitle[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("DangerPanelTextBrush");
        _errorPanel.Children.Add(errorTitle);

        _errorText = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        _errorText[!TextBlock.FontSizeProperty] = new DynamicResourceExtension("CaptionFontSize");
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

            var htmlContent = await LoadChartHtmlAsync();

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

    /// <summary>
    /// 读取内嵌的 K 线页面，并把 KLineCharts 脚本内联进去（NavigateToString 无法解析相对路径的脚本引用）。
    /// 两个文件都随 Assets\** 打进程序集，缺失即构建问题，异常交给 InitializeChartAsync 统一展示。
    /// </summary>
    private static async Task<string> LoadChartHtmlAsync()
    {
        var html = await LoadRawAssetTextAsync("kline_chart.html");
        var bundleScript = await LoadRawAssetTextAsync("klinecharts.min.js");
        return html.Replace("/* __KLINECHARTS_BUNDLE__ */", bundleScript, StringComparison.Ordinal);
    }

    private static async Task<string> LoadRawAssetTextAsync(string fileName)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://MarketAssistant/Assets/Raw/{fileName}"));
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
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



