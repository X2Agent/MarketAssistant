namespace MarketAssistant.ViewModels;

/// <summary>
/// 顶栏行情条单项（当前为模拟数据，待接入真实指数服务后替换）
/// </summary>
public class IndexTickerItemViewModel
{
    /// <summary>
    /// 指数名称（如"上证"、"BTC"）
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 最新点位/价格
    /// </summary>
    public string Price { get; }

    /// <summary>
    /// 涨跌幅文本（含正负号）
    /// </summary>
    public string ChangeText { get; }

    /// <summary>
    /// 是否上涨（决定涨跌配色）
    /// </summary>
    public bool IsUp { get; }

    /// <summary>
    /// 是否下跌（决定涨跌配色）
    /// </summary>
    public bool IsDown { get; }

    public IndexTickerItemViewModel(string name, string price, string changeText, bool isUp)
    {
        Name = name;
        Price = price;
        ChangeText = changeText;
        IsUp = isUp;
        IsDown = !isUp;
    }
}