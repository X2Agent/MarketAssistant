using DotLiquid;
using MarketAssistant.Agents;
using MarketAssistant.ViewModels;
using System.Collections.ObjectModel;

namespace MarketAssistant.Views;

public partial class RawDataView : ContentView
{
    #region 私有字段
    private readonly Timer _debounceTimer;
    private readonly TimeSpan _debounceDelay = TimeSpan.FromMilliseconds(300);
    private volatile bool _isUpdating;
    private CancellationTokenSource? _updateCancellationTokenSource;

    // 分析师配置 
    private static readonly Dictionary<AnalysisAgents, AnalystConfig> AnalystConfigs = new()
    {
        { AnalysisAgents.CoordinatorAnalystAgent, new("最终的投资决策建议", "#007bff", 1) },
        { AnalysisAgents.FundamentalAnalystAgent, new("基本面分析", "#6f42c1", 2) },
        { AnalysisAgents.TechnicalAnalystAgent, new("技术分析", "#dc3545", 3) },
        { AnalysisAgents.NewsEventAnalystAgent, new("新闻事件分析", "#fd7e14", 4) },
        { AnalysisAgents.MarketSentimentAnalystAgent, new("市场情绪分析", "#6610f2", 5) },
        { AnalysisAgents.FinancialAnalystAgent, new("财务分析", "#20c997", 6) }
    };
    #endregion

    #region 内部类型
    private record AnalystConfig(string Title, string Color, int Order);
    #endregion

    #region 公共属性
    public static readonly BindableProperty AnalysisMessagesProperty =
        BindableProperty.Create(nameof(AnalysisMessages), typeof(ObservableCollection<AnalysisMessage>), typeof(RawDataView), null, propertyChanged: OnAnalysisMessagesChanged);

    public ObservableCollection<AnalysisMessage> AnalysisMessages
    {
        get => (ObservableCollection<AnalysisMessage>)GetValue(AnalysisMessagesProperty);
        set => SetValue(AnalysisMessagesProperty, value);
    }
    #endregion

    #region 构造函数和生命周期
    public RawDataView()
    {
        InitializeComponent();
        _debounceTimer = new Timer(OnDebounceTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
    }

    ~RawDataView()
    {
        _debounceTimer?.Dispose();
        _updateCancellationTokenSource?.Cancel();
        _updateCancellationTokenSource?.Dispose();
    }
    #endregion

    #region 事件处理
    private static void OnAnalysisMessagesChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not RawDataView control) return;

        // 取消旧集合的订阅
        if (oldValue is ObservableCollection<AnalysisMessage> oldMessages)
        {
            oldMessages.CollectionChanged -= control.OnAnalysisMessagesCollectionChanged;
        }

        // 订阅新集合的变更事件
        if (newValue is ObservableCollection<AnalysisMessage> newMessages)
        {
            newMessages.CollectionChanged += control.OnAnalysisMessagesCollectionChanged;
            // 初始加载
            control.ScheduleUpdate();
        }
    }

    private void OnAnalysisMessagesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        ScheduleUpdate();
    }

    private void OnDebounceTimerElapsed(object? state)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await UpdateWebViewContentAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"更新WebView内容时发生错误: {ex.Message}");
            }
        });
    }
    #endregion

    #region 私有方法
    private void ScheduleUpdate()
    {
        // 防抖：重置计时器
        _debounceTimer.Change(_debounceDelay, Timeout.InfiniteTimeSpan);
    }

    private async Task UpdateWebViewContentAsync()
    {
        // 如果已经在更新中，则取消
        if (_isUpdating) return;

        _isUpdating = true;
        try
        {
            // 取消之前的更新任务
            _updateCancellationTokenSource?.Cancel();
            _updateCancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _updateCancellationTokenSource.Token;

            var messages = AnalysisMessages;
            if (messages == null || !messages.Any())
            {
                return;
            }

            // 获取分析师消息
            var analystMessages = GetAnalystMessages(messages);
            if (!analystMessages.Any())
            {
                return;
            }

            // 生成HTML内容
            var htmlContent = await GenerateCombinedHtmlContentAsync(analystMessages, cancellationToken);

            if (cancellationToken.IsCancellationRequested) return;

            // 更新WebView
            CombinedAnalysisWebView.Source = new HtmlWebViewSource { Html = htmlContent };
        }
        catch (OperationCanceledException)
        {
            // 忽略取消异常
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"更新WebView内容时发生错误: {ex.Message}");
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private Dictionary<AnalysisAgents, string> GetAnalystMessages(ObservableCollection<AnalysisMessage> messages)
    {
        var result = new Dictionary<AnalysisAgents, string>();

        foreach (var analystType in AnalystConfigs.Keys)
        {
            var analystName = analystType.ToString();
            var latestMessage = messages
                .Where(m => string.Equals(m.Sender, analystName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(m => m.Timestamp)
                .FirstOrDefault();

            if (latestMessage != null && !string.IsNullOrWhiteSpace(latestMessage.Content))
            {
                result[analystType] = latestMessage.Content;
            }
        }

        return result;
    }

    private async Task<string> GenerateCombinedHtmlContentAsync(
        Dictionary<AnalysisAgents, string> analystMessages,
        CancellationToken cancellationToken = default)
    {
        var sections = new List<object>();

        // 按配置的顺序添加分析师内容
        foreach (var (analystType, config) in AnalystConfigs.OrderBy(kvp => kvp.Value.Order))
        {
            if (analystMessages.TryGetValue(analystType, out var content) && !string.IsNullOrWhiteSpace(content))
            {
                cancellationToken.ThrowIfCancellationRequested();

                sections.Add(new
                {
                    title = config.Title,
                    content = Markdig.Markdown.ToHtml(content),
                    color = config.Color
                });
            }
        }

        if (!sections.Any())
        {
            return "<html><body><p>暂无分析内容</p></body></html>";
        }

        // 使用Liquid模板渲染HTML
        var templateContent = await LoadEmbeddedTemplateAsync("AnalysisReport.liquid");
        var template = Template.Parse(templateContent);
        var data = new { sections };

        return template.Render(Hash.FromAnonymousObject(data));
    }

    private async Task<string> LoadEmbeddedTemplateAsync(string fileName)
    {
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync(fileName);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            throw new FileNotFoundException($"Template file '{fileName}' not found: {ex.Message}", ex);
        }
    }
    #endregion
}