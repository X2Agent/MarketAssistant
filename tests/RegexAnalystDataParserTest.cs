using MarketAssistant.Views.Parsers;

namespace TestMarketAssistant;

/// <summary>
/// 正则表达式分析师解析器测试类
/// </summary>
[TestClass]
public class RegexAnalystDataParserTest
{
    private RegexAnalystDataParser _parser;

    [TestInitialize]
    public void Initialize()
    {
        _parser = new RegexAnalystDataParser();
    }

    /// <summary>
    /// 测试协调分析师解析
    /// </summary>
    [TestMethod]
    public async Task TestCoordinatorAnalystParsing()
    {
        var testContent = @"
        股票基本信息  
        股票代码：aaaa  
        当前价格：19.5元  

        各维度分析汇总  
        基本面评估：8分 技术布局完善、国际龙头地位强，但毛利率承压  
        技术面评估：7分 4.8元支撑明显，5.3元压力待突破  
        市场情绪评估：6分 投资者对传统基建板块关注度降低  
        财务健康评估：7分 现金流稳健但高负债率限制弹性  
        新闻事件影响评估：6分 国内钢铁政策超预期影响有限  

        综合评分：7.2分 略优于行业平均的8.1x PE水平  

        分析师共识与分歧  
        核心共识：1.冶金工程龙头地位稳固；2.低估值具备安全边际 [共识度8分]  
        主要分歧：海外订单增长可持续性争议  
        短期/中期/长期观点一致性：高 一致认可技术升级主线  

        最终投资建议  
        综合评级：买入  
        目标区间：5.3-6.0元  
        建议仓位：中等仓位  
        上涨空间：+8.9% / 下跌风险：-14.3%  
        置信度：72%  
        风险水平：中风险  

        核心投资逻辑与风险  
        投资亮点：绝对行业龙头地位+全产业链降本优势  
        关键风险：地缘局势影响海外订单  
        关键指标：海外新签订单增速/季度毛利率变化  
        操作建议：4.8-5.1元分批建仓，突破5.3元加仓
        ";

        var result = await _parser.ParseDataAsync(testContent);
        Assert.IsNotNull(result.StockSymbol, "解析结果不应为空");
    }

    /// <summary>
    /// 测试完整分析报告解析
    /// </summary>
    [TestMethod]
    public async Task TestCompleteAnalysisReportParsing()
    {
        var testContent = @"
        股票基本信息  
        股票代码：TSLA  
        当前价格：245.8元  

        各维度分析汇总  
        基本面评估：8分 技术布局完善、国际龙头地位强
        技术面评估：7分 支撑明显，压力待突破  
        市场情绪评估：6分 投资者关注度降低  
        财务健康评估：7分 现金流稳健但负债率偏高

        综合评分：7.2分

        分析师共识与分歧  
        核心共识：龙头地位稳固，低估值具备安全边际
        主要分歧：海外订单增长可持续性争议  

        最终投资建议  
        综合评级：买入  
        目标区间：280-320元  
        风险水平：中风险  
        置信度：75%

        核心投资逻辑与风险  
        投资亮点：绝对行业龙头地位；全产业链优势；技术护城河深厚
        关键风险：地缘局势影响；原材料价格波动；汇率风险
        操作建议：分批建仓；设置止损位；关注季报数据
        ";

        var result = await _parser.ParseDataAsync(testContent);

        // 验证基本信息
        Assert.AreEqual("TSLA", result.StockSymbol);
        Assert.AreEqual(7.2f, result.OverallScore, 0.01f);
        Assert.AreEqual(75.0f, result.ConfidencePercentage, 0.01f);
        Assert.AreEqual("买入", result.InvestmentRating);
        Assert.AreEqual("280-320元", result.TargetPrice);
        Assert.AreEqual("中风险", result.RiskLevel);

        // 验证维度评分
        Assert.AreEqual(4, result.DimensionScores.Count);
        Assert.IsTrue(result.DimensionScores.ContainsKey("基本面"));
        Assert.AreEqual(8.0f, result.DimensionScores["基本面"], 0.01f);

        // 验证共识和分歧
        Assert.IsTrue(result.ConsensusInfo.Contains("龙头地位稳固"));
        Assert.IsTrue(result.DisagreementInfo.Contains("海外订单"));

        // 验证投资亮点和风险
        Assert.IsTrue(result.InvestmentHighlights.Count >= 3);
        Assert.IsTrue(result.RiskFactors.Count >= 3);
        Assert.IsTrue(result.OperationSuggestions.Count >= 3);
    }
}