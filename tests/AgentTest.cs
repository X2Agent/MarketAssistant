using MarketAssistant.Agents;
using MarketAssistant.Infrastructure.Factories;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace TestMarketAssistant;

[TestClass]
public class AgentTest : BaseAgentTest
{
    public const string FundamentalAnalystContent = @"
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
    public const string NewsEventAnalystContent = @"
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
    public const string FinancialAnalystContent = @"
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

    [TestMethod]
    public async Task TestCoordinatorAnalystAsync()
    {
        var prompt = "请对股票 sh601606 进行综合分析，提供投资建议。";
        var agentFactory = _serviceProvider.GetRequiredService<IAnalystAgentFactory>();
        var agent = agentFactory.CreateAnalyst(AnalysisAgent.CoordinatorAnalyst);
        
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "以下是其他分析师的分析结果：\n\n" + 
                $"基本面分析：{FundamentalAnalystContent}\n\n" +
                $"新闻事件分析：{NewsEventAnalystContent}\n\n" +
                $"财务分析：{FinancialAnalystContent}"),
            new(ChatRole.User, prompt)
        };

        var response = await agent.RunAsync(messages);
        var result = response.Messages.LastOrDefault()?.Text ?? string.Empty;

        Assert.IsFalse(string.IsNullOrWhiteSpace(result));
        Console.WriteLine(result);
    }

    [TestMethod]
    public async Task TestFinancialAnalystAsync()
    {
        var agentFactory = _serviceProvider.GetRequiredService<IAnalystAgentFactory>();
        var agent = agentFactory.CreateAnalyst(AnalysisAgent.FinancialAnalyst);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "请对股票 sh601606 进行财务分析，提供投资建议。")
        };

        var response = await agent.RunAsync(messages);
        var result = response.Messages.LastOrDefault()?.Text ?? string.Empty;

        Assert.IsFalse(string.IsNullOrWhiteSpace(result));
        Console.WriteLine(result);
    }

    [TestMethod]
    public async Task TestFundamentalAnalystAsync()
    {
        var agentFactory = _serviceProvider.GetRequiredService<IAnalystAgentFactory>();
        var agent = agentFactory.CreateAnalyst(AnalysisAgent.FundamentalAnalyst);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "请对股票 sh601606 进行分析，提供投资建议。")
        };

        var response = await agent.RunAsync(messages);
        var result = response.Messages.LastOrDefault()?.Text ?? string.Empty;

        Assert.IsFalse(string.IsNullOrWhiteSpace(result));
        Console.WriteLine(result);
    }

    [TestMethod]
    public async Task TestTechnicalAnalystAsync()
    {
        var agentFactory = _serviceProvider.GetRequiredService<IAnalystAgentFactory>();
        var agent = agentFactory.CreateAnalyst(AnalysisAgent.TechnicalAnalyst);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "请对股票 sz002594 进行技术分析，提供投资建议。如果获取技术指标失败则模拟假的技术指标来测试")
        };

        var response = await agent.RunAsync(messages);
        var result = response.Messages.LastOrDefault()?.Text ?? string.Empty;

        Assert.IsFalse(string.IsNullOrWhiteSpace(result));
        Console.WriteLine(result);
    }

    [TestMethod]
    public async Task TestMarketSentimentAnalystAsync()
    {
        var agentFactory = _serviceProvider.GetRequiredService<IAnalystAgentFactory>();
        var agent = agentFactory.CreateAnalyst(AnalysisAgent.MarketSentimentAnalyst);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "请对股票 sz002594 进行市场情绪分析，忽略技术指标，提供投资建议。")
        };

        var response = await agent.RunAsync(messages);
        var result = response.Messages.LastOrDefault()?.Text ?? string.Empty;

        Assert.IsFalse(string.IsNullOrWhiteSpace(result));
        Console.WriteLine(result);
    }

    [TestMethod]
    public async Task TestNewsEventAnalystAsync()
    {
        var agentFactory = _serviceProvider.GetRequiredService<IAnalystAgentFactory>();
        var agent = agentFactory.CreateAnalyst(AnalysisAgent.NewsEventAnalyst);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "请对股票 sz002594 进行新闻事件分析，提供投资建议。")
        };

        var response = await agent.RunAsync(messages);
        var result = response.Messages.LastOrDefault()?.Text ?? string.Empty;

        Assert.IsFalse(string.IsNullOrWhiteSpace(result));
        Console.WriteLine(result);
    }
}
