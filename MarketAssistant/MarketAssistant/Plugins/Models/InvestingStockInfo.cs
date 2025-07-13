namespace MarketAssistant.Plugins.Models;

/// <summary>
/// 投资网站股票信息模型
/// </summary>
public class InvestingStockInfo
{
    /// <summary>
    /// 股票名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 股票代码/符号
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 当前价格
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// 涨跌幅百分比
    /// </summary>
    public decimal ChangePercent { get; set; }

    /// <summary>
    /// 涨跌价格
    /// </summary>
    public decimal ChangePrice { get; set; }

    /// <summary>
    /// 市值（亿）
    /// </summary>
    public decimal MarketCap { get; set; }

    /// <summary>
    /// 市盈率 PE
    /// </summary>
    public decimal PERatio { get; set; }

    /// <summary>
    /// 市净率 PB
    /// </summary>
    public decimal PBRatio { get; set; }

    /// <summary>
    /// 股息率 %
    /// </summary>
    public decimal DividendYield { get; set; }

    /// <summary>
    /// 成交量
    /// </summary>
    public decimal Volume { get; set; }

    /// <summary>
    /// 成交额
    /// </summary>
    public decimal Turnover { get; set; }

    /// <summary>
    /// 52周最高价
    /// </summary>
    public decimal High52Week { get; set; }

    /// <summary>
    /// 52周最低价
    /// </summary>
    public decimal Low52Week { get; set; }

    /// <summary>
    /// 行业
    /// </summary>
    public string Sector { get; set; } = string.Empty;

    /// <summary>
    /// 国家/地区
    /// </summary>
    public string Country { get; set; } = string.Empty;

    /// <summary>
    /// 交易所
    /// </summary>
    public string Exchange { get; set; } = string.Empty;

    /// <summary>
    /// 货币单位
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// 评级（如果有）
    /// </summary>
    public string Rating { get; set; } = string.Empty;

    /// <summary>
    /// 数据更新时间
    /// </summary>
    public DateTime UpdateTime { get; set; } = DateTime.Now;

    /// <summary>
    /// 股票链接
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// 返回股票信息的简要描述
    /// </summary>
    public override string ToString()
    {
        return $"{Name} ({Symbol}) - 价格: {Price:F2}, 涨跌: {ChangePercent:F2}%, 市值: {MarketCap:F2}亿, PE: {PERatio:F2}";
    }
}

/// <summary>
/// 股票筛选请求参数
/// </summary>
public class StockScreeningRequest
{
    /// <summary>
    /// 国家/地区
    /// </summary>
    public string Country { get; set; } = "中国";

    /// <summary>
    /// 最小市值（亿）
    /// </summary>
    public decimal? MinMarketCap { get; set; }

    /// <summary>
    /// 最大市值（亿）
    /// </summary>
    public decimal? MaxMarketCap { get; set; }

    /// <summary>
    /// 最小市盈率
    /// </summary>
    public decimal? MinPE { get; set; }

    /// <summary>
    /// 最大市盈率
    /// </summary>
    public decimal? MaxPE { get; set; }

    /// <summary>
    /// 最小股息率
    /// </summary>
    public decimal? MinDividend { get; set; }

    /// <summary>
    /// 最大股息率
    /// </summary>
    public decimal? MaxDividend { get; set; }

    /// <summary>
    /// 行业筛选
    /// </summary>
    public string? Sector { get; set; }

    /// <summary>
    /// 股票类型
    /// </summary>
    public string? StockType { get; set; }

    /// <summary>
    /// 排序方式
    /// </summary>
    public string SortBy { get; set; } = "涨幅";

    /// <summary>
    /// 返回数量限制
    /// </summary>
    public int Limit { get; set; } = 50;
}

/// <summary>
/// 股票筛选结果
/// </summary>
public class StockScreeningResult
{
    /// <summary>
    /// 股票列表
    /// </summary>
    public List<InvestingStockInfo> Stocks { get; set; } = new();

    /// <summary>
    /// 总数量
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// 筛选条件摘要
    /// </summary>
    public string FilterSummary { get; set; } = string.Empty;

    /// <summary>
    /// 数据来源
    /// </summary>
    public string Source { get; set; } = "investing.com";

    /// <summary>
    /// 获取时间
    /// </summary>
    public DateTime FetchTime { get; set; } = DateTime.Now;
}
