using MarketAssistant.Agents.StockSelection.Models;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services.StockScreener.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.StockSelection.Executors;

/// <summary>
/// 步骤1: 生成股票筛选条件的 Executor（基于 Executor<TInput, TOutput> 模式）
/// 将用户需求或新闻内容转换为结构化的筛选条件 JSON
/// </summary>
public sealed class GenerateCriteriaExecutor : Executor<StockSelectionWorkflowRequest, CriteriaGenerationResult>
{
    private readonly IChatClientFactory _chatClientFactory;
    private readonly ILogger<GenerateCriteriaExecutor> _logger;

    /// <summary>
    /// 用于生成 JSON Schema 的序列化选项（camelCase 属性命名）
    /// </summary>
    private static readonly JsonSerializerOptions SchemaOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// 用于反序列化 AI 响应的序列化选项（Web 默认配置 + 大小写不敏感）
    /// </summary>
    private static readonly JsonSerializerOptions DeserializationOptions = new(JsonSerializerOptions.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public GenerateCriteriaExecutor(
        IChatClientFactory chatClientFactory,
        ILogger<GenerateCriteriaExecutor> logger) : base("GenerateCriteria")
    {
        _chatClientFactory = chatClientFactory ?? throw new ArgumentNullException(nameof(chatClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override async ValueTask<CriteriaGenerationResult> HandleAsync(
        StockSelectionWorkflowRequest input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[步骤1/3] 将{Type}转换为筛选条件",
            input.IsNewsAnalysis ? "新闻内容" : "用户需求");

        try
        {
            // 构建 System Prompt
            string systemPrompt = input.IsNewsAnalysis
                ? BuildNewsAnalysisSystemPrompt()
                : BuildUserRequirementSystemPrompt();

            // 构建 User Prompt
            string userPrompt = BuildUserPrompt(input);

            // 创建 ChatClient
            var chatClient = _chatClientFactory.CreateClient();

            // 创建结构化输出的 JSON Schema
            var schema = AIJsonUtilities.CreateJsonSchema(typeof(StockCriteria), serializerOptions: SchemaOptions);

            // 配置聊天选项
            var chatOptions = new ChatOptions
            {
                ResponseFormat = ChatResponseFormat.ForJsonSchema(
                    schema: schema,
                    schemaName: "StockCriteria",
                    schemaDescription: "包含筛选条件、市场、行业和数量限制的股票筛选参数"),
                Temperature = 0.1f,
                MaxOutputTokens = input.IsNewsAnalysis ? 3500 : 2000
            };

            // 执行聊天完成
            var response = await chatClient.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, systemPrompt),
                    new ChatMessage(ChatRole.User, userPrompt)
                ],
                chatOptions,
                cancellationToken);

            // 获取响应文本并反序列化为 StockCriteria 对象以验证格式
            var criteria = JsonSerializer.Deserialize<StockCriteria>(response.Text, DeserializationOptions);
            if (criteria == null)
            {
                throw new InvalidOperationException("筛选条件 JSON 解析失败");
            }
            _logger.LogInformation("[步骤1/3] 筛选条件生成完成，包含 {Count} 个条件",
                criteria.Criteria?.Count ?? 0);
            // 返回结果
            return new CriteriaGenerationResult
            {
                Criteria = criteria,
                OriginalRequest = input
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[步骤1/3] 生成筛选条件失败");
            // 检查是否为 FriendlyException，如果是则直接抛出
            if (ex is FriendlyException)
            {
                throw;
            }

            // 将其他异常包装为 FriendlyException，以便在 UI 层显示友好错误
            throw new FriendlyException(ex.Message);
        }
    }

    /// <summary>
    /// 构建用户需求分析的系统提示词
    /// </summary>
    private string BuildUserRequirementSystemPrompt()
    {
        return """
## 主要任务
分析用户需求，生成合理的筛选条件。

## 需求转换规则

### 市值类别
- 大盘股/蓝筹股 → mc >= 30000000000
- 中盘股 → mc: 15000000000-30000000000  
- 小盘股 → mc < 15000000000
- 市值X亿以上 → mc >= X*100000000

### 估值指标
- 价值股/低估值/便宜 → pettm < 40, pb < 4
- 成长股/高成长 → npay > 10, oiy > 10
- 高ROE/盈利能力强 → roediluted > 10
- 低市盈率 → pettm < 40
- 低市净率 → pb < 4
- 市盈率X倍以下 → pettm < X
- 市净率X倍以下 → pb < X

### 财务表现
- 业绩好/盈利增长 → npay > 10
- 营收增长 → oiy > 10
- 高股息/分红股 → dy_l > 2
- 每股净资产高 → bps > 10
- 净利润增长X%以上 → npay > X
- 营收增长X%以上 → oiy > X
- 股息率X%以上 → dy_l > X

### 市场表现
- 活跃股/成交活跃 → amount > 100000000, tr > 2
- 强势股 → pct60 > 20
- 近期涨幅大 → pct20 > 10
- 抗跌股 → pct20 > -5
- 近X日涨幅大于Y% → pctX > Y
- 成交额X亿以上 → amount > X*100000000
- 换手率X%以上 → tr > X

### 价格相关
- 股价X元以下 → current < X
- 股价X元以上 → current > X
- 低价股 → current < 10
- 中价股 → current: 10-50
- 高价股 → current > 50

### 行业选择规则

**识别原则**：
1. 如果用户**未明确提到**任何行业、领域或板块 → 使用 **All**（默认值）
2. 如果用户**明确提到**某个具体行业或领域 → 根据下表选择最匹配的行业枚举值

**行业映射表**（优先匹配具体行业）：
- 科技股/AI/人工智能/芯片/云计算/5G/大数据/软件 → **ComputerEquipment** 或 **SoftwareDevelopment**
- 半导体/芯片制造/集成电路/存储器 → **Semiconductor**
- 新能源/电动车电池/光伏/风电/储能 → **Battery** 或 **PhotovoltaicEquipment** 或 **WindPowerEquipment**
- 医药/新药研发/疫苗/生物技术 → **ChemicalPharmaceutical** 或 **BiologicalProducts** 或 **MedicalDevices**
- 消费/白酒/饮料/食品 → **Liquor** 或 **BeveragesDairy** 或 **FoodProcessing**
- 银行/金融 → **JointStockBank** 或 **StateBanks**
- 房地产/地产 → **RealEstateDevelopment**
- 汽车/新能源车 → **PassengerVehicles** 或 **AutoParts**
- 通信/5G/通信设备 → **CommunicationEquipment** 或 **CommunicationServices**
- 电力/电网/发电 → **Power**
- 化工 → **ChemicalMaterials** 或 **ChemicalProducts**
- 机械/工程机械 → **ConstructionMachinery** 或 **SpecializedEquipment**
- 家电 → **WhiteAppliances** 或 **SmallAppliances**
- 没有提到任何行业 → **All**

## 支持的筛选指标

### 基本指标 (basic) - 15个
- mc: 总市值
- fmc: 流通市值
- pettm: 市盈率TTM
- pelyr: 市盈率LYR
- pb: 市净率MRQ
- psr: 市销率(倍)
- roediluted: 净资产收益率
- bps: 每股净资产
- eps: 每股收益
- netprofit: 净利润
- total_revenue: 营业收入
- dy_l: 股息收益率
- npay: 净利润同比增长
- oiy: 营业收入同比增长
- niota: 总资产报酬率

### 行情指标 (market) - 14个
- current: 当前价
- pct: 当日涨跌幅
- pct5: 近5日涨跌幅
- pct10: 近10日涨跌幅
- pct20: 近20日涨跌幅
- pct60: 近60日涨跌幅
- pct120: 近120日涨跌幅
- pct250: 近250日涨跌幅
- pct_current_year: 年初至今涨跌幅
- amount: 当日成交额
- volume: 本日成交量
- volume_ratio: 当日量比
- tr: 当日换手率
- chgpct: 当日振幅

### 雪球指标 (snowball) - 9个
- follow: 累计关注人数
- tweet: 累计讨论次数
- deal: 累计交易分享数
- follow7d: 一周新增关注
- tweet7d: 一周新增讨论数
- deal7d: 一周新增交易分享数
- follow7dpct: 一周关注增长率
- tweet7dpct: 一周讨论增长率
- deal7dpct: 一周交易分享增长率
""";
    }

    /// <summary>
    /// 构建新闻分析的系统提示词
    /// </summary>
    private string BuildNewsAnalysisSystemPrompt()
    {
        return """
## 任务
分析新闻内容，识别相关行业，判断情感倾向，并生成对应的股票筛选条件。

## 新闻行业识别规则

**识别原则**：
1. 如果新闻**明确涉及某个具体行业或技术领域** → 根据下表选择最相关的行业枚举值
2. 只有在新闻是**宏观经济、多行业政策、跨行业综合报道**时 → 使用 **All**

**行业映射表**（根据新闻内容关键词选择，优先匹配具体行业）：

**科技类新闻**（AI、人工智能、芯片、云计算、5G、大数据、软件）→ **ComputerEquipment** 或 **SoftwareDevelopment**  
**半导体新闻**（芯片制造、集成电路、存储器）→ **Semiconductor**  
**新能源类新闻**（电动车电池、光伏、风电、储能）→ **Battery** 或 **PhotovoltaicEquipment** 或 **WindPowerEquipment**  
**医药类新闻**（新药研发、疫苗、生物技术）→ **ChemicalPharmaceutical** 或 **BiologicalProducts** 或 **MedicalDevices**  
**消费类新闻**（白酒、饮料、食品）→ **Liquor** 或 **BeveragesDairy** 或 **FoodProcessing**  
**银行类新闻**（银行业务、金融政策）→ **JointStockBank** 或 **StateBanks**  
**房地产新闻**（地产政策、房价调控）→ **RealEstateDevelopment**  
**汽车新闻**（传统汽车、新能源车）→ **PassengerVehicles** 或 **AutoParts**  
**通信新闻**（5G、通信设备、运营商）→ **CommunicationEquipment** 或 **CommunicationServices**  
**电力新闻**（电网、发电、新能源）→ **Power**  
**化工新闻**（化学原料、精细化工）→ **ChemicalMaterials** 或 **ChemicalProducts**  
**机械新闻**（工程机械、专用设备）→ **ConstructionMachinery** 或 **SpecializedEquipment**  
**家电新闻**（空调、冰箱、小家电）→ **WhiteAppliances** 或 **SmallAppliances**  
**宏观经济、货币政策、多行业新闻** → **All**

## 指标说明

### 基本指标
- mc: 总市值（元，如100亿=10000000000）  
- npay: 净利润同比增长率（%）  
- roediluted: 净资产收益率（%）  
- pb: 市净率  
- pettm: 市盈率TTM  
- dy_l: 股息收益率（%）  

### 市场指标
- amount: 成交额（元，如1亿=100000000）  
- tr: 换手率（%）  
- pct20: 近20日涨跌幅（%）  
- pct60: 近60日涨跌幅（%）  

## 情感判断与筛选策略

**积极新闻 → 成长股策略**  
- npay > 15  
- ro ediluted > 12  
- pct20 > -5  

**政策利好 → 龙头股策略**  
- mc > 10000000000  
- roediluted > 10  
- pettm < 30  

**技术突破 → 创新股策略**  
- amount > 200000000  
- tr > 2  
- pct60 > 0  

**业绩利好 → 价值股策略**  
- npay > 20  
- pb < 3  
- roediluted > 15  

**中性/消极新闻 → 防御股策略**  
- dy_l > 2  
- pb < 2  
- roediluted > 8  
""";
    }

    /// <summary>
    /// 构建用户提示词
    /// </summary>
    private string BuildUserPrompt(StockSelectionWorkflowRequest input)
    {
        if (input.IsNewsAnalysis)
        {
            return $"""
                新闻内容：
                {input.Content}

                推荐股票数量限制：{input.MaxRecommendations}

                请根据新闻内容生成股票筛选条件。
                """;
        }
        else
        {
            return $"""
                用户需求：
                {input.Content}

                推荐股票数量限制：{input.MaxRecommendations}

                请根据用户需求生成股票筛选条件。
                """;
        }
    }
}

