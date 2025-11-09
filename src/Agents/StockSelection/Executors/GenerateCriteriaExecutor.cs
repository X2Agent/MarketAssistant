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
            var schema = AIJsonUtilities.CreateJsonSchema(typeof(StockCriteria));

            // 配置聊天选项（使用结构化输出）
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
            var criteria = JsonSerializer.Deserialize<StockCriteria>(response.Text, JsonSerializerOptions.Web);
            if (criteria == null)
            {
                throw new InvalidOperationException("筛选条件 JSON 解析失败");
            }
            _logger.LogInformation("[步骤1/3] 筛选条件生成完成，包含 {Count} 个条件",
                criteria.Criteria?.Count ?? 0);

            // 返回结果（框架会自动传递给下游）
            return new CriteriaGenerationResult
            {
                Criteria = criteria,
                OriginalRequest = input
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[步骤1/3] 生成筛选条件失败");
            throw new InvalidOperationException($"生成筛选条件失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 构建用户需求分析的系统提示词
    /// </summary>
    private string BuildUserRequirementSystemPrompt()
    {
        return """
你是一个专业的需求转换助手，负责将用户的文字需求转换为标准的股票筛选条件。

## 需求转换规则

### 市值类别
- 大盘股/蓝筹股 → mc >= 100000000000
- 中盘股 → mc: 10000000000-100000000000  
- 小盘股 → mc < 10000000000
- 市值X亿以上 → mc >= X*100000000

### 估值指标
- 价值股/低估值/便宜 → pettm < 15, pb < 2
- 成长股/高成长 → npay > 20, oiy > 15
- 高ROE/盈利能力强 → roediluted > 15
- 低市盈率 → pettm < 20
- 低市净率 → pb < 3

### 财务表现
- 业绩好/盈利增长 → npay > 10
- 营收增长 → oiy > 10
- 高股息/分红股 → dy_l > 2
- 每股净资产高 → bps > 10

### 市场表现
- 活跃股/成交活跃 → amount > 100000000, tr > 2
- 强势股 → pct60 > 20
- 近期涨幅大 → pct20 > 10
- 抗跌股 → pct20 > -5

### 价格相关
- 股价X元以下 → current < X
- 股价X元以上 → current > X
- 低价股 → current < 10
- 中价股 → current: 10-50
- 高价股 → current > 50

## 支持的筛选指标

### 基本指标 (basic)
- mc: 总市值
- fmc: 流通市值
- pettm: 市盈率TTM
- pelyr: 市盈率LYR
- pb: 市净率MRQ
- psr: 市销率
- roediluted: 净资产收益率
- bps: 每股净资产
- eps: 每股收益
- netprofit: 净利润
- total_revenue: 营业收入
- dy_l: 股息收益率
- npay: 净利润同比增长
- oiy: 营业收入同比增长
- niota: 总资产报酬率

### 行情指标 (market)
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

### 雪球指标 (snowball)
- follow: 累计关注人数
- tweet: 累计讨论次数
- deal: 累计交易分享数
- follow7d: 一周新增关注
- tweet7d: 一周新增讨论数
- deal7d: 一周新增交易分享数

## 输出要求
分析用户需求，生成符合 StockCriteria 格式的结构化输出。
- criteria 数组包含具体的筛选条件
- market 字段固定为 "全部A股"
- industry 字段为具体行业名称或空字符串
- limit 字段为推荐股票数量限制
""";
    }

    /// <summary>
    /// 构建新闻分析的系统提示词
    /// </summary>
    private string BuildNewsAnalysisSystemPrompt()
    {
        return """
你是专业的财经新闻分析师，负责将新闻内容转换为精确的股票筛选条件。

## 任务
分析新闻内容，识别相关行业，判断情感倾向，并生成对应的股票筛选条件。

## 新闻类型识别与行业映射
根据新闻内容关键词，选择相关性最高的单个行业（必须使用以下精确名称）：

**科技类新闻**（AI、芯片、云计算、5G、大数据、软件）→ **计算机设备** 或 **软件开发**  
**半导体新闻**（芯片制造、集成电路、存储器）→ **半导体**  
**新能源类新闻**（电动车电池、光伏、风电、储能）→ **电池** 或 **光伏设备** 或 **风电设备**  
**医药类新闻**（新药研发、疫苗、生物技术）→ **化学制药** 或 **生物制品** 或 **医疗器械**  
**消费类新闻**（白酒、饮料、食品）→ **白酒** 或 **饮料乳品** 或 **食品加工**  
**银行类新闻**（银行业务、金融政策）→ **股份制银行** 或 **国有大型银行**  
**房地产新闻**（地产政策、房价调控）→ **房地产开发**  
**汽车新闻**（传统汽车、新能源车）→ **乘用车** 或 **汽车零部件**  
**通信新闻**（5G、通信设备、运营商）→ **通信设备** 或 **通信服务**  
**电力新闻**（电网、发电、新能源）→ **电力**  
**化工新闻**（化学原料、精细化工）→ **化学原料** 或 **化学制品**  
**机械新闻**（工程机械、专用设备）→ **工程机械** 或 **专用设备**  
**家电新闻**（空调、冰箱、小家电）→ **白色家电** 或 **小家电**  

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
- roediluted > 12  
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

## 输出要求
生成符合 StockCriteria 格式的结构化输出。
- criteria 数组包含具体的筛选条件
- market 字段固定为 "全部A股"
- industry 字段为单个行业名称
- limit 字段为推荐股票数量限制
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

