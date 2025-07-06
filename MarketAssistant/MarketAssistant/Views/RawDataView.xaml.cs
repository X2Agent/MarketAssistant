using DotLiquid;
using MarketAssistant.Agents;
using MarketAssistant.ViewModels;
using System.Collections.ObjectModel;

namespace MarketAssistant.Views;

public partial class RawDataView : ContentView
{
    // 分析消息列表
    public static readonly BindableProperty AnalysisMessagesProperty =
        BindableProperty.Create(nameof(AnalysisMessages), typeof(ObservableCollection<AnalysisMessage>), typeof(RawDataView), null, propertyChanged: OnAnalysisMessagesChanged);

    public ObservableCollection<AnalysisMessage> AnalysisMessages
    {
        get => (ObservableCollection<AnalysisMessage>)GetValue(AnalysisMessagesProperty);
        set => SetValue(AnalysisMessagesProperty, value);
    }

    public RawDataView()
    {
        InitializeComponent();
    }

    private static void OnAnalysisMessagesChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var control = (RawDataView)bindable;
        var oldMessages = oldValue as ObservableCollection<AnalysisMessage>;
        var newMessages = newValue as ObservableCollection<AnalysisMessage>;

        // 取消旧集合的订阅
        if (oldMessages != null)
        {
            oldMessages.CollectionChanged -= control.OnAnalysisMessagesCollectionChanged;
        }

        // 订阅新集合的变更事件
        if (newMessages != null)
        {
            newMessages.CollectionChanged += control.OnAnalysisMessagesCollectionChanged;
        }
    }

    // 获取指定分析师的消息
    private static string GetAnalystMessage(ObservableCollection<AnalysisMessage> messages, string analystName)
    {
        return messages
            .Where(m => m.Sender == analystName)
            .OrderByDescending(m => m.Timestamp)
            .FirstOrDefault()?.Content ?? "";
    }

    // 处理集合内容变化事件
    private void OnAnalysisMessagesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // 当集合内容变化时更新WebView
        if (sender is ObservableCollection<AnalysisMessage> messages)
        {
            UpdateWebViewContent(messages);
        }
    }

    // 更新WebView内容
    private async void UpdateWebViewContent(ObservableCollection<AnalysisMessage> messages)
    {
        if (messages != null && messages.Any())
        {
            // 获取各类分析师的消息
            var fundamentalAnalysis = GetAnalystMessage(messages, nameof(AnalysisAgents.FundamentalAnalystAgent));
            var technicalAnalysis = GetAnalystMessage(messages, nameof(AnalysisAgents.TechnicalAnalystAgent));
            var newsAnalysis = GetAnalystMessage(messages, nameof(AnalysisAgents.NewsEventAnalystAgent));
            var marketSentimentAnalysis = GetAnalystMessage(messages, nameof(AnalysisAgents.MarketSentimentAnalystAgent));
            var financialAnalysis = GetAnalystMessage(messages, nameof(AnalysisAgents.FinancialAnalystAgent));
            var coordinatorAnalyst = GetAnalystMessage(messages, nameof(AnalysisAgents.CoordinatorAnalystAgent));

            // 合并所有分析内容为一个HTML文档
            var combinedContent = await GenerateCombinedHtmlContentAsync(
                coordinatorAnalyst,
                fundamentalAnalysis,
                technicalAnalysis,
                newsAnalysis,
                marketSentimentAnalysis,
                financialAnalysis);

            // 直接使用完整的HTML内容，避免重复的Markdown转换
            CombinedAnalysisWebView.Source = new HtmlWebViewSource
            {
                Html = combinedContent
            };
        }
    }

    // 生成合并的HTML内容
    private async Task<string> GenerateCombinedHtmlContentAsync(string coordinatorAnalyst, string fundamentalAnalysis,
        string technicalAnalysis, string newsAnalysis, string marketSentimentAnalysis, string financialAnalysis)
    {
        var sections = new List<object>();

        if (!string.IsNullOrEmpty(coordinatorAnalyst))
            sections.Add(new { title = "最终的投资决策建议", content = Markdig.Markdown.ToHtml(coordinatorAnalyst), color = "#007bff" });

        if (!string.IsNullOrEmpty(fundamentalAnalysis))
            sections.Add(new { title = "基本面分析", content = Markdig.Markdown.ToHtml(fundamentalAnalysis), color = "#6f42c1" });

        if (!string.IsNullOrEmpty(technicalAnalysis))
            sections.Add(new { title = "技术分析", content = Markdig.Markdown.ToHtml(technicalAnalysis), color = "#dc3545" });

        if (!string.IsNullOrEmpty(newsAnalysis))
            sections.Add(new { title = "新闻事件分析", content = Markdig.Markdown.ToHtml(newsAnalysis), color = "#fd7e14" });

        if (!string.IsNullOrEmpty(marketSentimentAnalysis))
            sections.Add(new { title = "市场情绪分析", content = Markdig.Markdown.ToHtml(marketSentimentAnalysis), color = "#6610f2" });

        if (!string.IsNullOrEmpty(financialAnalysis))
            sections.Add(new { title = "财务分析", content = Markdig.Markdown.ToHtml(financialAnalysis), color = "#20c997" });

        // 使用Liquid模板渲染HTML
        var templateContent = await LoadEmbeddedTemplateAsync("AnalysisReport.liquid");
        var template = Template.Parse(templateContent);

        var data = new { sections };
        return template.Render(Hash.FromAnonymousObject(data));
    }

    // 加载嵌入的模板文件
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
}