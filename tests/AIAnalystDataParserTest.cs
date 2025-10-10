using MarketAssistant.Views.Parsers;

namespace TestMarketAssistant;

/// <summary>
/// AI分析师解析器测试类
/// </summary>
[TestClass]
public class AIAnalystDataParserTest : BaseKernelTest
{
    private IAnalystDataParser _aiParser;

    [TestInitialize]
    public void Initialize()
    {
        BaseInitialize(); // 调用基类初始化方法
        _aiParser = new AIAnalystDataParser(_kernel);
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


        var result = await _aiParser!.ParseDataAsync(testContent);
        Assert.IsNotNull(result.StockSymbol, "解析结果不应为空");
        //_logger?.LogInformation($"协调分析解析成功: {result.StockSymbol}, 评级: {result.InvestmentRating}");
    }

}