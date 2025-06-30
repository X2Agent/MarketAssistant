using MarketAssistant.Agents;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;

namespace TestMarketAssistant;

[TestClass]
public class AgentTest : BaseKernelTest
{
    private Kernel _kernel = null!;

    [TestInitialize]
    public void Initialize()
    {
        _kernel = CreateKernelWithChatCompletion();
    }

    public const string FundamentalAnalystContent = @"
        根据现有数据对比亚迪（sz002594）分析如下：

        股票基本信息
        股票代码/名称：比亚迪
        当前价格：300元
        价格变动：+6%

        **公司基本面**  
        行业定位：新能源汽车/锂电池 [成长性9/10]  
        核心业务：电动汽车、电池技术 [业务质量8/10]  
        盈利能力：毛利率20.47%（需确认单位）、净利率9.79%（需确认单位），盈利趋势优  
        财务稳健性：资产负债率70.71%，现金流健康

        **行业与竞争**  
        行业生命周期：成长期 [确信度8/10]  
        市场地位：行业龙头（新能源车市占率全球第二）  
        核心竞争力：技术领先（刀片电池/CTB技术）、品牌优势 [强度9/10]  
        长期壁垒：高（专利技术矩阵+垂直整合能力）

        **增长潜力**  
        增长驱动：全球新能源车渗透率提升、储能业务扩张 [持续性8/10]  
        当前估值：PE 20.47×（低于行业25×均值）  
        投资评级：买入（合理估值空间+20%）  
        投资亮点：垂直供应链成本优势、525项电池专利  
        关键风险：碳酸锂等原材料价格波动 [风险强度7/10]

        注：财务数据单位疑为万元，建议关注Q3季度毛利率波动情况及现金流管理。技术面因KDJ数据缺失无法分析，可观察近期成交量变动。
        ";
    public const string TechnicalAnalystContent = @"
        ### 图表形态与趋势
        当前趋势：**震荡区间** [趋势强度评分：7/10]  
        关键形态：**上升楔形**（近期价格在收敛通道内波动）/[可靠性评分：8/10]  
        时间框架：日线 [与周线/月线一致性评分：6/10]  

        ### 关键价位分析
        当前价格：**343.74**  
        核心支撑位：340.00（近期低点）/[335.00（长期支撑）] [支撑强度：8/10]  
        核心阻力位：350.00（前高压力）/[360.00（强阻力）] [阻力强度：7/10]  
        突破概率：**维持震荡** [概率评分：7/10]  

        ### 技术指标综合解读
        - **趋势指标（模拟）**：5日MA（344.5）与10日MA（342.0）呈黏合状态，未明显多头或空头排列。[信号可靠性：6/10]  
        - **动量指标（模拟）**：KDJ指标（J值85，前次90）在超买区形成顶背离，MACD（DIF -0.5，DEA -1.0）零轴下方金叉但力度不足。[信号可靠性：5/10]  
        - **成交量**：缩量 [量价关系：不健康（放量下跌可能预示弱势）]  
        - **指标一致性**：低（趋势震荡与超买动量信号矛盾）  

        ### 交易策略建议  
        技术面评级：**卖出**  
        操作方向：**卖出**  
        目标价位：**335.00-340.00**  
        止损位置：**350.00**（若突破阻力转为观望）  
        持仓周期：**短期** [风险等级：中]  

        **逻辑说明**：上升楔形顶背离预示短期回调风险，缩量下跌叠加超买动量，建议在支撑位前获利了结或逢高减仓，跌破340则打开下行空间。若MACD DEAD叉或量能放大可加强看跌信号。
        ";
    public const string MarketSentimentAnalystContent = @"
        市场情绪评估  
        主导情绪：恐惧 [4分]  
        恐慌与信心：VIX水平中性，但机构资金持续流出；投资者信心：中 → 低（主力资金连续20日净流出）  
        整体氛围：悲观 [6分]  

        资金流向分析  
        主力资金：净流出 [-368,630万（近10日）]  
        机构动向：减仓（中型基金大幅流出1亿，大型基金小幅流入800万）  
        北向资金：无数据（依赖外资流向需额外调取）  
        融资融券：融资余额持稳，杠杆率中等  

        投资者行为分析  
        主要行为偏差：损失厌恶 [7分]  
        散户特征：杀跌 [6分]  
        机构一致性：分歧（内外资金方向不一）  
        风险偏好：低 → 更低（防御情绪主导）  

        短期市场洞察与策略  
        市场节奏：快速轮动（资金短期避险驱动）  
        热点与机会：无（核心赛道拥挤度下降，建议回避）；波段套利需等待情绪底部信号  
        操作建议：卖出；仓位：保守（现金优先）  
        最佳时机与区间：待主力资金转净流入或政策催化；当前无目标区间；止损线按行业跌幅均值下移  

        需规避心理陷阱：避免因短期资金流出而过度恐慌性抛售，忽视其行业龙头地位与基本面。（比亚迪属于新能源汽车赛道核心资产，财报尚未发布前需警惕超跌后的反弹机会）
        ";
    public const string NewsEventAnalystContent = @"
         **事件解读与定性**  
        1. **事件类型：行业政策/市场消息**  
           **事件概要：比亚迪等布局超快充技术，行业竞争升级**  
           **信息来源与可信度：新浪财经（可信度8）**  
           **事件性质：利好（重要性7/10）**  
        2. **事件类型：公司治理**  
           **事件概要：舆论质疑“比亚迪病”反映供应链/管理问题**  
           **信息来源与可信度：周律微金融（可信度6）**  
           **事件性质：中性偏利空（重要性5/10）**  
        3. **事件类型：市场消息**  
           **事件概要：比亚迪降价引发天然橡胶价格急跌**  
           **信息来源与可信度：日经中文网（可信度9）**  
           **事件性质：利空（重要性6/10）**  

        ---

        **影响评估与市场反应**  
        1. **基本面影响：正面（+4/10）**  
           超快充技术若落地，将提升产品竞争力，但短期需关注成本压力和渗透率。  
        2. **情绪影响：中性偏负面（-3/10）**  
           降价争议可能加剧市场对价格战的担忧，但技术创新预期或部分抵消。  
        3. **影响范围与时长：公司与行业性/中期（6-12个月）**  
        4. **市场反应：过度反应（股价短期震荡可能超预期）**  

        ---

        **投资启示与建议**  
        1. **投资影响评估：风险**（短期需警惕情绪波动，长期技术路线仍关键）  
           **核心逻辑：短期争议与估值压力或压制股价，中长期看技术优势与规模效应。**  
        2. **应对策略建议：观望**  
           **操作：关注技术落地进展与二季度财报，若回调至240-250元区间可择机试仓。**  
        3. **关注重点：Q2毛利率变化；超快充车型量产时间表。**  
        4. **关键风险：原材料成本超预期上涨；行业竞争导致盈利恶化。**  

        ---  
        *注：基于现有新闻分析，实际需结合技术面及财务数据综合判断。*
        ";
    public const string FinancialAnalystContent = @"
        ### 财务健康评估
            - **偿债能力**  
              流动比率：1.12 / 速动比率：1.16  
              **评分：2/10**  
              短期偿债能力极弱，流动比率<1.5，速动比率<1.8，低于安全阈值，信用风险显著。  
    
            - **资产负债结构**  
              资产负债率：70.7%（同比持平）  
              **债务结构评估：风险**  
              负债率常年高于70%，杠杆水平危险。  
              **整体财务稳健性：弱评分：3/10**  
              高负债与弱流动性叠加，财务稳定性差。
    
            ### 盈利质量分析
            - **盈利能力**  
              毛利率：29.0%（假设zysr为毛利额） / 净利率：7.5%（稳步上升）  
              **评分：5/10 稳步增长但基础薄弱**  
              净利率逐季提升，但杠杆推高ROE，可持续性存疑。  
    
            - **投入产出效率**  
              ROE：10.6% / ROA：4.4%（行业均值对比：中）  
              **利润质量：低评分：3/10**  
              利润中非经常性损益占比高，2024Q4达到21.73元/股（可能为异常干预）。
    
            ### 现金流评估
            - **经营现金流**  
              净额：0.17亿（2024Q4） / 净利比值：0.05（评分：1/10）  
              **自由现金流：负值**  
              趋势持续恶化，依赖筹资保运营  
              **现金转换周期：数据缺失**  
    
            ### 财务风险预警
            - **主要风险指标**：资产负债率>70%，经营现金流与利润严重背离  
            - **财务造假风险：中评分：4/10**  
              高ROE与低现金流及蹊跷非经常性损益需进一步验证  
            - **建议关注**：流动负债同比激增+147%，应收账款/存货周转异常
    
            ### 投资建议
            **评级：减持（2.8/10）**  
            高负债叠加流动性危机（流动比率<1.5）、利润质量不佳，现金流连续季度为负，财务风险集中。需重点验证非经常性收益来源及资本结构合理性前谨慎观望。
        ";

    [TestMethod]
    public async Task TestCoordinatorAnalystAsync()
    {
        var agent = CreateAgentFromYaml("CoordinatorAnalystAgent");
        agent.Arguments.Add("history", new List<ChatMessageContent> {
            new ChatMessageContent(AuthorRole.Assistant, FundamentalAnalystContent)
            {
                AuthorName = nameof(AnalysisAgents.FundamentalAnalystAgent)
            },
            new ChatMessageContent(AuthorRole.Assistant, TechnicalAnalystContent)
            {
                AuthorName = nameof(AnalysisAgents.TechnicalAnalystAgent)
            },
            new ChatMessageContent(AuthorRole.Assistant, MarketSentimentAnalystContent)
            {
                AuthorName = nameof(AnalysisAgents.MarketSentimentAnalystAgent)
            },
            new ChatMessageContent(AuthorRole.Assistant, NewsEventAnalystContent)
            {
                AuthorName = nameof(AnalysisAgents.NewsEventAnalystAgent)
            },
            new ChatMessageContent(AuthorRole.Assistant, FinancialAnalystContent)
            {
                AuthorName = nameof(AnalysisAgents.FinancialAnalystAgent)
            }
        });
        var content = await agent.InvokeAsync().ToListAsync();

        Assert.IsTrue(content.Count > 0);
        Console.WriteLine(content.First().Message);
    }

    [TestMethod]
    public async Task TestFinancialAnalystAsync()
    {
        var agent = CreateAgentFromYaml("FinancialAnalystAgent");

        var content = await agent.InvokeAsync(new ChatMessageContent(AuthorRole.User,
            "请对股票 sz002594 进行财务分析，提供投资建议。")).ToListAsync();

        Assert.IsTrue(content.Count > 0);
        Console.WriteLine(content.First().Message);
    }

    [TestMethod]
    public async Task TestFundamentalAnalystAsync()
    {
        var agent = CreateAgentFromYaml("FundamentalAnalystAgent");

        var content = await agent.InvokeAsync(new ChatMessageContent(AuthorRole.User,
            "请对股票 sz002594 进行分析，提供投资建议。")).ToListAsync();

        Assert.IsTrue(content.Count > 0);
        Console.WriteLine(content.First().Message);
    }

    [TestMethod]
    public async Task TestTechnicalAnalystAsync()
    {
        var agent = CreateAgentFromYaml("TechnicalAnalystAgent");

        var content = await agent.InvokeAsync(new ChatMessageContent(AuthorRole.User,
            "请对股票 sz002594 进行技术分析，提供投资建议。如果获取技术指标失败则模拟假的技术指标来测试")).ToListAsync();

        Assert.IsTrue(content.Count > 0);
        Console.WriteLine(content.First().Message);
    }

    [TestMethod]
    public async Task TestMarketSentimentAnalystAsync()
    {
        var agent = CreateAgentFromYaml("MarketSentimentAnalystAgent");

        var content = await agent.InvokeAsync(new ChatMessageContent(AuthorRole.User,
            "请对股票 sz002594 进行市场情绪分析，忽略技术指标，提供投资建议。")).ToListAsync();

        Assert.IsTrue(content.Count > 0);
        Console.WriteLine(content.First().Message);
    }

    [TestMethod]
    public async Task TestNewsEventAnalystAsync()
    {
        var agent = CreateAgentFromYaml("NewsEventAnalystAgent");

        var content = await agent.InvokeAsync(new ChatMessageContent(AuthorRole.User,
            "请对股票 sz002594 进行新闻事件分析，提供投资建议。")).ToListAsync();

        Assert.IsTrue(content.Count > 0);
        Console.WriteLine(content.First().Message);
    }

    /// <summary>
    /// 从YAML文件创建ChatCompletionAgent
    /// </summary>
    /// <param name="agentName">代理名称</param>
    /// <returns>ChatCompletionAgent实例</returns>
    private ChatCompletionAgent CreateAgentFromYaml(string agentName)
    {
        string agentYamlPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "MarketAssistant", "MarketAssistant", "Agents", "yaml", $"{agentName}.yaml");
        if (!File.Exists(agentYamlPath))
        {
            throw new Exception($"未找到分析师配置文件: {agentYamlPath}。请确保已正确配置并放置在Agents/yaml目录下。");
        }

        string yamlContent = File.ReadAllText(agentYamlPath);
        PromptTemplateConfig templateConfig = KernelFunctionYaml.ToPromptTemplateConfig(yamlContent);

        var globalGuidelines = @"
        ## 分析准则
        - 采用1-10分量化评估
        - 提供具体价格点位和数值区间
        - 控制总字数300字内
        - 直接输出专业分析，无需询问
        ";

        ChatCompletionAgent chatCompletionAgent =
            new ChatCompletionAgent(templateConfig, new KernelPromptTemplateFactory())
            {
                Kernel = _kernel,
                Arguments = new KernelArguments
                {
                    { "global_analysis_guidelines", globalGuidelines },
                }
            };

        return chatCompletionAgent;
    }
}
