namespace MarketAssistant.Agents.InvestmentSelection.Strategies;

/// <summary>
/// 资产数据字典的扩展方法，用于在格式化资产数据时统一添加非零字段
/// </summary>
public static class AssetDataExtensions
{
    /// <summary>
    /// 当 value 不为 0 时，将其按指定精度和除数转换后添加到字典中。
    /// 用于过滤掉无意义的零值字段，减少输出 JSON 体积。
    /// </summary>
    /// <param name="data">目标字典</param>
    /// <param name="key">字典键</param>
    /// <param name="value">原始数值（为 0 时跳过）</param>
    /// <param name="decimals">保留小数位数，默认 2</param>
    /// <param name="divisor">除数（用于单位换算，如分→元），默认 1 表示不换算</param>
    public static void AddIfNotZero(this Dictionary<string, object> data, string key, decimal value, int decimals = 2, decimal divisor = 1)
    {
        if (value != 0)
        {
            var convertedValue = divisor != 1 ? value / divisor : value;
            data[key] = Math.Round(convertedValue, decimals);
        }
    }
}
