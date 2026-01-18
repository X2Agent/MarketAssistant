using MarketAssistant.Agents.InvestmentSelection.Models;
using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Agents.InvestmentSelection.Strategies;

/// <summary>
/// 虚拟币筛选条件生成策略
/// </summary>
public class CryptoCriteriaGenerationStrategy : ICriteriaGenerationStrategy<CryptoCriteria>
{
    private static readonly JsonSerializerOptions DeserializationOptions = new(JsonSerializerOptions.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public MarketType SupportedMarketType => MarketType.Crypto;

    public string BuildUserRequirementSystemPrompt()
    {
        return """
## 主要任务
分析用户对虚拟币投资的需求，生成合理的筛选条件。

## 需求转换规则

### 市值类别
- 大盘币/主流币 → market_cap >= 10000000000 (100亿美元)
- 中盘币 → market_cap: 1000000000-10000000000 (10亿-100亿美元)
- 小盘币 → market_cap < 1000000000 (10亿美元以下)
- 市值X亿美元以上 → market_cap >= X*100000000

### 市场表现
- 活跃币/成交活跃 → volume_24h > 100000000 (1亿美元)
- 强势币 → price_change_7d > 10
- 近期涨幅大 → price_change_24h > 5
- 抗跌币 → price_change_24h > -5
- 24小时涨幅X% → price_change_24h > X
- 7天涨幅X% → price_change_7d > X
- 30天涨幅X% → price_change_30d > X

### 价格相关
- 低价币 → price < 1
- 中价币 → price: 1-100
- 高价币 → price > 100
- 价格X美元以下 → price < X
- 价格X美元以上 → price > X

### 市值排名
- 前10大币 → market_cap_rank <= 10
- 前50大币 → market_cap_rank <= 50
- 前100大币 → market_cap_rank <= 100

## 支持的筛选指标

### 基本指标
- market_cap: 市值（美元）
- volume_24h: 24小时交易量（美元）
- price: 当前价格（USDT）
- market_cap_rank: 市值排名
- circulating_supply: 流通量
- total_supply: 总供应量
- max_supply: 最大供应量

### 市场表现指标
- price_change_24h: 24小时涨跌幅（%）
- price_change_7d: 7天涨跌幅（%）
- price_change_30d: 30天涨跌幅（%）
- price_change_1y: 1年涨跌幅（%）
- volume_change_24h: 24小时交易量变化（%）

### 交易对设置
- 默认使用 USDT 作为基准货币
- 可指定其他交易对如 BTC、ETH 等

## 注意事项
- 虚拟币市场波动性大，筛选条件应合理
- 建议关注市值排名前100的主流币种
- 小盘币风险较高，需谨慎推荐
""";
    }

    public string BuildNewsAnalysisSystemPrompt()
    {
        return """
## 任务
分析新闻内容，识别相关虚拟币类别，判断情感倾向，并生成对应的筛选条件。

## 新闻类型识别规则

**区块链技术新闻**（以太坊升级、Layer2、DeFi协议）→ 关注 ETH、相关生态币种  
**比特币新闻**（BTC ETF、减半、机构采用）→ 关注 BTC  
**稳定币新闻**（USDT、USDC监管）→ 关注稳定币相关  
**NFT/元宇宙新闻**→ 关注相关概念币  
**交易所新闻**（币安、Coinbase）→ 关注平台币 BNB、交易所代币  
**监管新闻**（SEC、各国政策）→ 关注合规性强的主流币  
**技术创新新闻**（AI+区块链、新共识机制）→ 关注创新项目代币  

## 指标说明

### 基本指标
- market_cap: 市值（美元）
- volume_24h: 24小时交易量（美元）
- market_cap_rank: 市值排名
- price: 当前价格（USDT）

### 市场表现指标
- price_change_24h: 24小时涨跌幅（%）
- price_change_7d: 7天涨跌幅（%）
- price_change_30d: 30天涨跌幅（%）
- volume_change_24h: 24小时交易量变化（%）

## 情感判断与筛选策略

**积极新闻 → 成长币策略**
- price_change_7d > 0
- volume_24h > 50000000
- market_cap_rank <= 200

**技术突破 → 创新币策略**
- volume_change_24h > 50
- price_change_24h > -5
- market_cap_rank <= 100

**监管利好 → 主流币策略**
- market_cap_rank <= 20
- market_cap > 1000000000
- volume_24h > 100000000

**市场泡沫/消极新闻 → 防御策略**
- market_cap_rank <= 10
- volume_24h > 500000000
- price_change_30d > -10

**中性新闻 → 平衡策略**
- market_cap_rank <= 50
- volume_24h > 10000000
- price_change_7d > -20
""";
    }

    public string BuildUserPrompt(InvestmentSelectionWorkflowRequest request)
    {
        if (request.IsNewsAnalysis)
        {
            return $"""
                新闻内容：
                {request.Content}

                推荐虚拟币数量限制：{request.MaxRecommendations}

                请根据新闻内容生成虚拟币筛选条件。
                """;
        }
        else
        {
            return $"""
                用户需求：
                {request.Content}

                推荐虚拟币数量限制：{request.MaxRecommendations}

                请根据用户需求生成虚拟币筛选条件。
                """;
        }
    }

    public CryptoCriteria DeserializeCriteria(string json)
    {
        var criteria = JsonSerializer.Deserialize<CryptoCriteria>(json, DeserializationOptions);
        if (criteria == null)
        {
            throw new InvalidOperationException("虚拟币筛选条件 JSON 解析失败");
        }
        return criteria;
    }
}
