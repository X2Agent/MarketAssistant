using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Agents.InvestmentSelection.Strategies;

/// <summary>
/// 股票数据格式化器
/// </summary>
public class StockDataFormatter : IAssetDataFormatter
{
    public MarketType SupportedMarketType => MarketType.AShare;

    public string FormatAssetsForAnalysis(List<ScreenerAssetInfo> assets)
    {
        var simplifiedStocks = assets.OfType<ScreenerStockInfo>().Select(s =>
        {
            var data = new Dictionary<string, object>();

            data["名称"] = s.Name;
            data["代码"] = s.Symbol;

            data.AddIfNotZero("当前价_元", s.Current);
            data.AddIfNotZero("涨跌幅_百分比", s.Pct);
            data.AddIfNotZero("当日振幅_百分比", s.ChgPct);
            data.AddIfNotZero("总市值_亿元", s.Mc, 2, 100000000);
            data.AddIfNotZero("流通市值_亿元", s.Fmc, 2, 100000000);
            data.AddIfNotZero("成交额_亿元", s.Amount, 2, 100000000);
            data.AddIfNotZero("成交量_万股", s.Volume);
            data.AddIfNotZero("量比", s.VolumeRatio);
            data.AddIfNotZero("换手率_百分比", s.Tr);
            data.AddIfNotZero("市盈率TTM", s.PeTtm);
            data.AddIfNotZero("市盈率LYR", s.PeLyr);
            data.AddIfNotZero("市净率", s.Pb);
            data.AddIfNotZero("市销率", s.Psr);
            data.AddIfNotZero("每股净资产_元", s.Bps);
            data.AddIfNotZero("每股收益_元", s.Eps);
            data.AddIfNotZero("股息收益率_百分比", s.DyL);
            data.AddIfNotZero("净资产收益率ROE_百分比", s.RoeDiluted);
            data.AddIfNotZero("总资产报酬率_百分比", s.Niota);
            data.AddIfNotZero("净利润_亿元", s.NetProfit, 2, 100000000);
            data.AddIfNotZero("营业收入_亿元", s.TotalRevenue, 2, 100000000);
            data.AddIfNotZero("净利润同比增长_百分比", s.Npay);
            data.AddIfNotZero("营收同比增长_百分比", s.Oiy);
            data.AddIfNotZero("近5日涨跌幅_百分比", s.Pct5);
            data.AddIfNotZero("近10日涨跌幅_百分比", s.Pct10);
            data.AddIfNotZero("近20日涨跌幅_百分比", s.Pct20);
            data.AddIfNotZero("近60日涨跌幅_百分比", s.Pct60);
            data.AddIfNotZero("近120日涨跌幅_百分比", s.Pct120);
            data.AddIfNotZero("近250日涨跌幅_百分比", s.Pct250);
            data.AddIfNotZero("年初至今涨跌幅_百分比", s.PctCurrentYear);
            data.AddIfNotZero("累计关注人数", s.Follow, 0);
            data.AddIfNotZero("累计讨论次数", s.Tweet, 0);
            data.AddIfNotZero("累计交易分享数", s.Deal, 0);
            data.AddIfNotZero("一周新增关注", s.Follow7d, 0);
            data.AddIfNotZero("一周新增讨论数", s.Tweet7d, 0);
            data.AddIfNotZero("一周新增交易分享数", s.Deal7d, 0);
            data.AddIfNotZero("一周关注增长率_百分比", s.Follow7dPct);
            data.AddIfNotZero("一周讨论增长率_百分比", s.Tweet7dPct);
            data.AddIfNotZero("一周交易分享增长率_百分比", s.Deal7dPct);

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
