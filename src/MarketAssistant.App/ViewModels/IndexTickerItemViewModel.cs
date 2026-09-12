namespace MarketAssistant.ViewModels;

/// <summary>
/// 顶栏行情条单项：展示一个市场级聚合指标（指数点位/总市值/主导率/恐惧贪婪等）。
/// 由 <see cref="MarketAssistant.Services.Market.IndexQuoteItem"/> 映射而来，字符串已在服务侧格式化。
/// </summary>
public class IndexTickerItemViewModel
{
    /// <summary>
    /// 指标名称（如"上证指数"、"总市值"、"恐惧贪婪"）
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 指标数值文本（如"3,094.67"、"$2.41T"）
    /// </summary>
    public string Price { get; }

    /// <summary>
    /// 涨跌/分类文本（含正负号或中文分类）；无涨跌语义时可为空
    /// </summary>
    public string ChangeText { get; }

    /// <summary>
    /// 是否上涨（红涨配色）
    /// </summary>
    public bool IsUp { get; private set; }

    /// <summary>
    /// 是否下跌（绿跌配色）
    /// </summary>
    public bool IsDown { get; private set; }

    /// <summary>
    /// 是否中性/无涨跌语义（平盘或主导率、恐惧贪婪等，使用次级文本色）
    /// </summary>
    public bool IsNeutral { get; private set; }

    /// <summary>
    /// 是否存在需要展示的涨跌/分类文本
    /// </summary>
    public bool HasChangeText => !string.IsNullOrEmpty(ChangeText);

    public IndexTickerItemViewModel(string name, string price, string? changeText, bool isUp)
    {
        Name = name;
        Price = price;
        ChangeText = changeText ?? string.Empty;
        IsUp = isUp;
        IsDown = !isUp;
        IsNeutral = false;
    }

    /// <summary>
    /// 按趋势语义构造：Up 红、Down 绿、Flat/None 中性色。
    /// </summary>
    public static IndexTickerItemViewModel FromTrend(
        string name,
        string price,
        string? changeText,
        MarketAssistant.Services.Market.TickerTrend trend)
    {
        var item = new IndexTickerItemViewModel(name, price, changeText, trend == MarketAssistant.Services.Market.TickerTrend.Up);
        if (trend is MarketAssistant.Services.Market.TickerTrend.Flat or MarketAssistant.Services.Market.TickerTrend.None)
        {
            item.IsUp = false;
            item.IsDown = false;
            item.IsNeutral = true;
        }
        return item;
    }
}