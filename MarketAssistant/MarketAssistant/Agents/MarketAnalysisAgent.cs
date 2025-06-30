using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using static MarketAssistant.Agents.MarketAnalysisEvents;

namespace MarketAssistant.Agents;

/// <summary>
/// 优化版市场分析代理，采用任务列表方式执行分析流程
/// </summary>
public class MarketAnalysisAgent
{
    #region 事件定义

    /// <summary>
    /// 分析进度变化事件
    /// </summary>
    public event EventHandler<AnalysisProgressEventArgs> ProgressChanged;

    /// <summary>
    /// 分析消息事件
    /// </summary>
    public event EventHandler<ChatMessageContent> MessageReceived;

    #endregion

    #region 私有字段

    private readonly AnalystManager _analystManager;
    private readonly ILogger<MarketAnalysisAgent> _logger;
    private readonly string _copilot = "Copilot";

    // 当前进度信息
    private MarketAnalysisEvents.AnalysisProgressEventArgs _currentProgress = new();

    private List<ChatCompletionAgent> _analysts = new();

    // 存储分析消息
    private readonly List<ChatMessageContent> _analysisMessages = new();

    #endregion

    #region 构造函数

    public MarketAnalysisAgent(ILogger<MarketAnalysisAgent> logger, AnalystManager analystManager)
    {
        _logger = logger;
        _analystManager = analystManager;

        // 获取分析师团队
        _analysts = _analystManager.GetAnalysts();
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 分析股票（供ViewModel调用的方法）
    /// </summary>
    /// <param name="stockCode">股票代码</param>
    /// <returns>分析任务</returns>
    public async Task AnalyzeStockAsync(string stockCode)
    {
        await AnalysisAsync(stockCode);
    }

    /// <summary>
    /// 执行股票分析
    /// </summary>
    /// <param name="stockSymbol">股票代码</param>
    /// <returns>分析消息列表</returns>
    public async Task<List<ChatMessageContent>> AnalysisAsync(string stockSymbol)
    {
        try
        {
            // 初始化分析环境
            InitializeAnalysisEnvironment();

            // 构建分析提示词
            string prompt = BuildAnalysisPrompt(stockSymbol);

            // 执行分析过程
            await ExecuteAnalysisProcessAsync(prompt);

            // 更新进度为完成
            UpdateProgress(_copilot, 100, "分析完成");

            // 返回收集到的分析消息
            return _analysisMessages;
        }
        catch (Exception ex)
        {
            // 记录详细错误信息
            string errorMessage = $"分析股票 {stockSymbol} 时发生错误: {ex.Message}";
            if (ex.InnerException != null)
            {
                errorMessage += $"\n内部错误: {ex.InnerException.Message}";
            }

            // 更新进度为错误状态
            UpdateProgress(_copilot, 0, $"分析失败: {errorMessage}");
            _logger.LogError(errorMessage);
            // 添加错误消息到分析消息列表
            _analysisMessages.Add(new ChatMessageContent(AuthorRole.System, errorMessage)
            {
                AuthorName = _copilot
            });

            return _analysisMessages;
        }
    }

    #endregion

    #region 私有方法

    /// <summary>
    /// 初始化分析环境
    /// </summary>
    private void InitializeAnalysisEnvironment()
    {
        // 重置状态
        _analysisMessages.Clear();

        // 初始化进度信息
        _currentProgress = new AnalysisProgressEventArgs
        {
            CurrentAnalyst = "准备中",
            ProgressPercentage = 0,
            StageDescription = "正在准备分析环境"
        };

        // 触发进度变化事件
        OnProgressChanged(_currentProgress);
    }

    /// <summary>
    /// 构建分析提示词
    /// </summary>
    private string BuildAnalysisPrompt(string stockSymbol)
    {
        return $"请对股票 {stockSymbol} 进行专业分析，提供投资建议。";
    }

    /// <summary>
    /// 执行分析过程
    /// </summary>
    private async Task ExecuteAnalysisProcessAsync(string prompt)
    {
        // 更新进度
        UpdateProgress(nameof(AnalysisAgents.CoordinatorAnalystAgent), 0, "正在规划分析师任务");

        int analystCount = _analysts.Count;

        // 清空分析消息列表
        _analysisMessages.Clear();

        // 执行分析师讨论
        await _analystManager.ExecuteAnalystDiscussionAsync(prompt, messageContent =>
        {
            // 保存分析消息
            _analysisMessages.Add(messageContent);
            // 记录消息
            OnMessageReceived(messageContent);
            // 更新进度
            int progressPercentage = Math.Min(90, 10 + (_analysisMessages.Count * 80 / analystCount));
            UpdateProgress(messageContent.AuthorName, progressPercentage, $"{messageContent.AuthorName} 分析中");
        });

        // 更新进度为生成结果阶段
        UpdateProgress(_copilot, 95, "正在生成分析报告");
    }

    /// <summary>
    /// 更新进度并触发进度变化事件
    /// </summary>
    private void UpdateProgress(string analyst, int percentage, string description)
    {
        _currentProgress.CurrentAnalyst = analyst;
        _currentProgress.ProgressPercentage = percentage;
        _currentProgress.StageDescription = description;

        OnProgressChanged(_currentProgress);
    }

    /// <summary>
    /// 触发进度变化事件
    /// </summary>
    protected virtual void OnProgressChanged(MarketAnalysisEvents.AnalysisProgressEventArgs e)
    {
        ProgressChanged?.Invoke(this, e);
    }

    /// <summary>
    /// 触发消息接收事件
    /// </summary>
    protected virtual void OnMessageReceived(ChatMessageContent e)
    {
        MessageReceived?.Invoke(this, e);
    }

    #endregion
}