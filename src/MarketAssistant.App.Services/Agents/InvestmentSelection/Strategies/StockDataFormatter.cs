using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Agents.InvestmentSelection.Strategies;

/// <summary>
/// 股票数据格式化器
/// </summary>
public class StockDataFormatter : IAssetDataFormatter
{
    public MarketType SupportedMarketType => MarketType.AShare;

    /// <summary>
    /// 指标字段 → (显示名, 小数位数, 除数)
    /// </summary>
    private static readonly (string Field, string DisplayName, int Decimals, decimal Divisor)[] IndicatorConfig =
    [
        ("current", "当前价_元", 2, 1),
        ("pct", "涨跌幅_百分比", 2, 1),
        ("mc", "总市值_亿元", 2, 100_000_000),
        ("fmc", "流通市值_亿元", 2, 100_000_000),
        ("amount", "成交额_亿元", 2, 100_000_000),
        ("volume", "成交量_万股", 2, 1),
        ("chgpct", "当日振幅_百分比", 2, 1),
        ("volume_ratio", "量比", 2, 1),
        ("tr", "换手率_百分比", 2, 1),
        ("pettm", "市盈率TTM", 2, 1),
        ("pelyr", "市盈率LYR", 2, 1),
        ("pb", "市净率", 2, 1),
        ("psr", "市销率", 2, 1),
        ("bps", "每股净资产_元", 2, 1),
        ("eps", "每股收益_元", 2, 1),
        ("dy_l", "股息收益率_百分比", 2, 1),
        ("roediluted", "净资产收益率ROE_百分比", 2, 1),
        ("niota", "总资产报酬率_百分比", 2, 1),
        ("netprofit", "净利润_亿元", 2, 100_000_000),
        ("total_revenue", "营业收入_亿元", 2, 100_000_000),
        ("npay", "净利润同比增长_百分比", 2, 1),
        ("oiy", "营收同比增长_百分比", 2, 1),
        ("pct5", "近5日涨跌幅_百分比", 2, 1),
        ("pct10", "近10日涨跌幅_百分比", 2, 1),
        ("pct20", "近20日涨跌幅_百分比", 2, 1),
        ("pct60", "近60日涨跌幅_百分比", 2, 1),
        ("pct120", "近120日涨跌幅_百分比", 2, 1),
        ("pct250", "近250日涨跌幅_百分比", 2, 1),
        ("pct_current_year", "年初至今涨跌幅_百分比", 2, 1),
        ("follow", "累计关注人数", 0, 1),
        ("tweet", "累计讨论次数", 0, 1),
        ("deal", "累计交易分享数", 0, 1),
        ("follow7d", "一周新增关注", 0, 1),
        ("tweet7d", "一周新增讨论数", 0, 1),
        ("deal7d", "一周新增交易分享数", 0, 1),
        ("follow7dpct", "一周关注增长率_百分比", 2, 1),
        ("tweet7dpct", "一周讨论增长率_百分比", 2, 1),
        ("deal7dpct", "一周交易分享增长率_百分比", 2, 1),
    ];

    public string FormatAssetsForAnalysis(List<ScreenerAssetInfo> assets)
    {
        var simplifiedStocks = assets.OfType<ScreenerStockInfo>().Select(s =>
        {
            var data = new Dictionary<string, object>
            {
                ["名称"] = s.Name,
                ["代码"] = s.Symbol
            };

            foreach (var (field, displayName, decimals, divisor) in IndicatorConfig)
            {
                if (s.Indicators.TryGetValue(field, out var value))
                {
                    data.AddIfNotZero(displayName, value, decimals, divisor);
                }
            }

            return data;
        }).ToList();

        return JsonSerializer.Serialize(simplifiedStocks, JsonOptions.AssetFormatterOptions);
    }

    public string GetAnalysisInstructions(bool isNewsAnalysis)
    {
        return @"
你是专业的投资顾问，基于用户需求/新闻热点和股票数据提供投资建议。

## 核心职责
从筛选出的股票中进行多维度分析，输出结构化推荐报告。

## 评估维度（灵活权重）
1. **财务质量**：ROE、利润增长率、现金流、EPS/BPS
2. **估值水平**：PE/PB/PS 合理性、低估/高估判断、股息率
3. **市场表现**：涨跌幅、流动性（成交额/换手率）、技术面趋势
4. **需求匹配**：风险偏好、投资期限、行业偏好" + (isNewsAnalysis ? "，或新闻关联度" : "") + @"
5. **社交热度**：雪球关注/讨论及增长趋势（辅助参考）

## 分析要点
- 选出最优股票时，优先考虑财务健康度和估值合理性
- 推荐理由必须包含具体数据支撑，避免空泛描述
- 风险提示应针对个股和市场环境的具体风险
- 如无合适标的，可返回空推荐列表

## 输出格式
严格按 JSON Schema 定义的结构输出，所有必填字段不能为空或null。
";
    }
}
