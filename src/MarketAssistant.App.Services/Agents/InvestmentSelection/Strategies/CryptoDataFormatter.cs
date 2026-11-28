using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Agents.InvestmentSelection.Strategies;

/// <summary>
/// 虚拟币数据格式化器
/// </summary>
public class CryptoDataFormatter : IAssetDataFormatter
{
    public MarketType SupportedMarketType => MarketType.Crypto;

    public string FormatAssetsForAnalysis(List<ScreenerAssetInfo> assets)
    {
        var simplifiedCryptos = assets.OfType<ScreenerCryptoInfo>().Select(c =>
        {
            var data = new Dictionary<string, object>
            {
                ["名称"] = c.Name,
                ["代码"] = c.Symbol
            };

            data.AddIfNotZero("当前价格_USDT", c.Current, 8);
            data.AddIfNotZero("市值_亿美元", c.Mc, 2, 100000000);
            data.AddIfNotZero("完全稀释市值_亿美元", c.Fmc, 2, 100000000);

            data.AddIfNotZero("24h成交额_亿美元", c.Amount, 2, 100000000);

            data.AddIfNotZero("24h涨跌幅_百分比", c.Pct);
            data.AddIfNotZero("7天涨跌幅_百分比", c.PriceChange7d);
            data.AddIfNotZero("30天涨跌幅_百分比", c.PriceChange30d);
            data.AddIfNotZero("24h振幅_百分比", c.ChgPct);

            if (c.MarketCapRank > 0)
                data["市值排名"] = c.MarketCapRank;

            data.AddIfNotZero("流通供应量", c.CirculatingSupply, 0);
            data.AddIfNotZero("总供应量", c.TotalSupply, 0);
            if (c.MaxSupply.HasValue && c.MaxSupply.Value > 0)
                data["最大供应量"] = Math.Round(c.MaxSupply.Value, 0);

            return data;
        }).ToList();

        return JsonSerializer.Serialize(simplifiedCryptos, JsonOptions.AssetFormatterOptions);
    }

    public string GetAnalysisInstructions(bool isNewsAnalysis)
    {
        return @"
你是专业的加密货币投资顾问，基于用户需求/新闻热点和虚拟币数据提供投资建议。

## 核心职责
从筛选出的虚拟币中进行多维度分析，输出结构化推荐报告。

## 评估维度（灵活权重）
1. **市场表现**：市值与排名、成交额与流动性、24h/7天/30天涨跌幅、24h振幅
2. **代币供应**：流通/总/最大供应量、完全稀释市值与市值之比
3. **风险评估**：波动性、流动性、小市值风险" + (isNewsAnalysis ? "、新闻关联度" : "") + @"
4. **需求匹配**：用户需求或新闻主题与币种特征的匹配度

## 虚拟币特有分析要点
- 优先考虑市值排名靠前的主流币种
- 虚拟币市场波动性大，风险提示要充分

## 分析要点
- 仅依据候选数据分析；数据中没有的维度（如链上数据、团队背景、社区热度、市场情绪指数）写“数据不足”，不得编造
- 推荐理由必须包含具体数据支撑，避免空泛描述
- 风险提示应特别强调虚拟币的高波动性
- 如无合适标的，可返回空推荐列表

## 输出格式
严格按 JSON Schema 定义的结构输出，所有必填字段不能为空或null。
Symbol 必须原样使用候选数据中的“代码”字段（如 BTC），不要改写，也不要追加 /USDT 等交易对后缀。
";
    }
}
