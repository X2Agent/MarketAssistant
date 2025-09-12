using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

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
    /// 分析完成事件
    /// </summary>
    public event EventHandler<ChatMessageContent> AnalysisCompleted;

    #endregion

    #region 私有字段

    private readonly AnalystManager _analystManager;
    private readonly ILogger<MarketAnalysisAgent> _logger;
    private readonly string _copilot = "Copilot";

    // 当前进度信息
    private AnalysisProgressEventArgs _currentProgress = new();

    #endregion

    #region 构造函数

    public MarketAnalysisAgent(ILogger<MarketAnalysisAgent> logger, AnalystManager analystManager)
    {
        _logger = logger;
        _analystManager = analystManager;
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 执行股票分析
    /// </summary>
    /// <param name="stockSymbol">股票代码</param>
    /// <returns>分析消息列表</returns>
    public async Task<ChatHistory> AnalysisAsync(string stockSymbol)
    {
        try
        {
            // 初始化分析环境
            InitializeAnalysisEnvironment();

            // 构建分析提示词
            string prompt = BuildAnalysisPrompt(stockSymbol);

            // 执行分析过程
            await ExecuteAnalysisProcessAsync(prompt);
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
            UpdateProgress(_copilot, $"分析失败: {errorMessage}", false);
            _logger.LogError(errorMessage);
        }
        return _analystManager.History;
    }

    #endregion

    #region 私有方法

    /// <summary>
    /// 初始化分析环境
    /// </summary>
    private void InitializeAnalysisEnvironment()
    {
        // 初始化进度信息
        _currentProgress = new AnalysisProgressEventArgs
        {
            CurrentAnalyst = "准备中",
            StageDescription = "正在准备分析环境",
            IsInProgress = true
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
        UpdateProgress("分析师团队", "分析师分析中");

        var analystResults = await _analystManager.ExecuteAnalystDiscussionAsync(prompt, null);

        UpdateProgress("CoordinatorAnalystAgent", "CoordinatorAnalystAgent总结中");

        var coordinatorResult = await _analystManager.ExecuteCoordinatorAnalysisAsync(analystResults);

        UpdateProgress("系统", "CoordinatorAnalystAgent总结完成");

        // 触发分析完成事件
        AnalysisCompleted?.Invoke(this, coordinatorResult);
    }

    /// <summary>
    /// 更新进度并触发进度变化事件
    /// </summary>
    private void UpdateProgress(string analyst, string description, bool isInProgress = true)
    {
        _currentProgress.CurrentAnalyst = analyst;
        _currentProgress.StageDescription = description;
        _currentProgress.IsInProgress = isInProgress;

        OnProgressChanged(_currentProgress);
    }

    /// <summary>
    /// 触发进度变化事件
    /// </summary>
    protected virtual void OnProgressChanged(AnalysisProgressEventArgs e)
    {
        ProgressChanged?.Invoke(this, e);
    }

    #endregion
}