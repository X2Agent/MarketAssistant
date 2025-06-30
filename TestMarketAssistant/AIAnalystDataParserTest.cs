using MarketAssistant.Views.Parsers;
using Microsoft.SemanticKernel;

namespace TestMarketAssistant;

/// <summary>
/// AI分析师解析器测试类
/// </summary>
[TestClass]
public class AIAnalystDataParserTest : BaseKernelTest
{
    private IAnalystDataParser _aiParser;
    private Kernel _kernel = null!;

    [TestInitialize]
    public void Initialize()
    {
        BaseInitialize(); // 调用基类初始化方法
        _kernel = CreateKernelWithChatCompletion();
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
        var testContentV2 = @"
        各维度分析汇总
        基本面评估：8.5 [行业领先地位，毛利率稳升5%，ROIC持续高于行业均值]
        技术面评估：7.2 [突破60日均线关键阻力，MACD金叉形成，支撑位30.5-31.2区间稳固]
        市场情绪评估：6.8 [机构持仓比例达58%，但散户空头头寸较前月增加12%]
        财务健康评估：8.0 [债务/股本比降至15%以下，经营现金流年化增长22%]
        新闻事件影响评估：7.5 [近期获行业创新奖项，新产品测试数据超预期]
        综合评分：8.1 [优于行业平均20%，具备结构化上涨动能]

        分析师共识与分歧
        核心共识：[行业景气度维持2-3年，市占率突破临界规模] 9.2
        主要分歧：[估值方法选择：12位分析师用DCF，5位采用PEG模型] 财务估值方法论
        短期/中期/长期观点一致性：高中低 [短期看技术突破，中期看产能释放，长期存在替代技术变数]

        最终投资建议
        综合评级：买入
        目标价格区间：38.5-41.2 [基于DCF 7.8倍PS与同行业可比公司PE分位]
        建议仓位：中等仓位 [考虑估值与技术面共振但存在资金分流风险]
        上涨空间：28.6% / 下跌风险：11.4%
        置信度：76%
        风险水平：中风险

        核心投资逻辑与风险
        投资亮点：[22nm工艺量产突破，北美订单有望翻倍增长]
        [核心专利布局形成技术护城河]
        关键风险因素：[地缘政治影响供应链，HBM新能效比突破可能]
        关键监测指标：[良品率是否突破90%节点，北美客户订单环比增速]
        操作建议：[突破33.5压力位建仓20%，持有至2024Q3季报前]
        ";

        var result = await _aiParser!.ParseDataAsync(testContent);
        var resultV2 = await _aiParser!.ParseDataAsync(testContentV2);
        Assert.IsNotNull(result.StockSymbol, "解析结果不应为空");
        Assert.IsNotNull(resultV2.StockSymbol, "解析结果不应为空");
        //_logger?.LogInformation($"协调分析解析成功: {result.StockSymbol}, 评级: {result.InvestmentRating}");
    }

}