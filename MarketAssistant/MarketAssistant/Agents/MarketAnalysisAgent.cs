using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
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

    private ChatCompletionAgent _coordinatorAgent;

    #endregion

    #region 构造函数

    public MarketAnalysisAgent(ILogger<MarketAnalysisAgent> logger, AnalystManager analystManager)
    {
        _logger = logger;
        _analystManager = analystManager;

        // 创建协调分析师
        _coordinatorAgent = analystManager.CreateCoordinatorAgent();
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

        var coordinatorResult = await ExecuteCoordinatorAnalysisAsync(analystResults);

        UpdateProgress("系统", "CoordinatorAnalystAgent总结完成");

        // 触发分析完成事件
        OnAnalysisCompleted(coordinatorResult);
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

    /// <summary>
    /// 触发分析完成事件
    /// </summary>
    protected virtual void OnAnalysisCompleted(ChatMessageContent result)
    {
        AnalysisCompleted?.Invoke(this, result);
    }

    /// <summary>
    /// 执行协调分析师分析
    /// </summary>
    private async Task<ChatMessageContent> ExecuteCoordinatorAnalysisAsync(string[] analystResults)
    {
        string FundamentalAnalystContent = @"
        股票代码/名称：sh601606 长城军工  
        当前价格：29.3 CNY  
        价格变动：-4.09%（-1.2元）  

        公司基本面：  
        行业定位：航天军工（成长性评分4/10）  
        核心业务：弹药制造、民品研发（质量评分6/10）  
        盈利能力：Seasoned净利率-3.21%、-18.48%、-1.25%，趋势差  
        财务稳健性：资产负债率64.51%，现金流紧张  

        行业与竞争：  
        行业生命周期：成长期（确信度6/10）  
        市场地位：第二梯队（国企+军工集团资源整合）  
        核心竞争力：政策+技术壁垒（评分7/10）  
        长期壁垒：中（军工业务订单独占性）  

        增长潜力与价值：  
        增长驱动：军民融合+国企改革（持续性4/10）  
        当前估值：PE -58.41，远高于行业均值-30  
        投资评级：减持（目标区间18-25元）  
        投资亮点：军工业务政策免税、短期资产重估  
        关键风险：连续5季度现金流净流出（均值-5600万）        
        ";
        string NewsEventAnalystContent = @"
         事件解读与定性：
        [突发事件] 7月3日长城军工股价异动且融资余额变动显著
        [市场传闻] 7月2日军工板块整体走低导致内蒙一机跌停
        [可信度8分/8分] [中性/利空] [5分/7分]
        事件性质：技术面风险预警（连续两日融资出现反向波动，且热榜排名变化明显）；行业性系统风险（军工板块基准龙头跌停引发个股连锁反应）

        影响评估与市场反应：
        基本面影响：中性（无实质业绩公告）[3分]（主要体现为市场流动性变化而非内在价值改变）
        情绪影响：负面（板块高位分歧）[6分]（军工细分赛道筹码松动下，投资者风险偏好下降）
        影响范围：行业性 [短期（7-15日）]
        市场预期反应：过度反应[1-2天内触达压力位]
        资金流向：净流出[7月2-3日累计超4.8亿主力资金撤离]

        投资启示与建议：
        投资影响：风险规避[技术破位+板块轮动中需防补跌]
        应对策略：卖出[建议跌破50日线28.0元止损，配置暂时转至前期超跌的光伏ETF]
        关注重点：[1]兵器工业集团混改方案启动（截至2025年Q2未披露进展；[2]军工板块ESG评级调整（美国彭博7月修订压力传导政策）
        关键风险：北向配置盘订单撤单（监测20日MACD绿柱是否突破1.2亿柱体）";
        string FinancialAnalystContent = @"
        【财务健康评估】  
        偿债能力：  
        流动比率：2.99（2024Q4）→2.66（2025Q1）  
        速动比率：0.99（2024Q4）→0.65（2025Q1）  
        评分：3/10（短期偿债能力恶化，速动比率跌破1）  

        资产负债结构：  
        资产负债率：77.26%（2024Q4）→76.76%（2025Q1）（持平）  
        债务结构：高负债依赖（75%+已持续3季度）→风险  
        整体稳健性：弱（评分4/10）  

        【盈利质量分析】  
        盈利能力：  
        毛利率：3.01（2025Q1）→3.56（2024Q3）（波动）  
        净利率：-28.24%（2024Q4）→-5.2%（2025Q1）（恶化）  
        趋势：下滑（评分1/10）  

        投入产出效率：  
        ROE：未披露→无法评估  
        ROA：-1.26%（2024Q4）→-5.2%（2025Q1）（行业低位）  
        利润质量：低（评分2/10，净利润连续5季度为负）  

        【现金流评估】  
        经营现金流：  
        净额：-15,623.84（2025Q1）→-19,481.88（2024Q3）  
        与净利润比值：-3.0（2025Q1）→-5.1（2024Q3）→质量评分1/10  
        自由现金流：连续3季度净流出（-5,491.8→-5,423.8→-5,491.8）→恶化  
        可持续性：1/10（现金转换周期延长至32天）  

        【财务风险预警】  
        主要风险：高资产负债率（76%+）、经营性现金流持续为负  
        造假风险：中（评分5/10，存在利润调节嫌疑）  
        建议关注：  
        1. 国企改革降杠杆成效（2024Q4负债总额44.17亿）  
        2. 军工订单落地节奏（2025Q1经营收入14.79亿同比-23%）  

        **投资建议**：当前估值（PE 22.93）与基本面严重背离，财务风险等级高（综合评分2/10）。建议回避，待国企改革与军品业务放量信号明确后再评估。        
        ";

        // 设置历史记录参数
        _coordinatorAgent.Arguments!.Add("history", new List<string> { FundamentalAnalystContent, NewsEventAnalystContent, FinancialAnalystContent });

        var agentResponses = await _coordinatorAgent.InvokeAsync().ToListAsync();

        _coordinatorAgent.Arguments["history"] = analystResults.ToList();

        var agentResponses2 = await _coordinatorAgent.InvokeAsync().ToListAsync();

        return agentResponses.LastOrDefault() ?? throw new InvalidOperationException("协调分析师未返回任何响应");
    }

    #endregion
}