namespace MarketAssistant.Infrastructure.Core;

/// <summary>
/// 股票代码格式转换工具类
/// </summary>
public static class StockSymbolConverter
{
    /// <summary>
    /// 将股票代码转换为财联社格式（如 SH600000、SZ000001）
    /// </summary>
    public static string ToClsFormat(string stockCode)
    {
        string digits = ExtractDigits(stockCode);
        string prefix = GetExchangePrefix(digits);
        return $"{prefix}{digits}";
    }

    /// <summary>
    /// 将股票代码转换为智图API格式（如 600000.SH、000001.SZ）
    /// </summary>
    public static string ToZhiTuFormat(string stockCode)
    {
        if (stockCode.Contains("."))
        {
            return stockCode.ToUpper();
        }

        if (stockCode.StartsWith("sz", StringComparison.OrdinalIgnoreCase) || 
            stockCode.StartsWith("sh", StringComparison.OrdinalIgnoreCase))
        {
            string code = stockCode.Substring(2);
            string market = stockCode.StartsWith("sz", StringComparison.OrdinalIgnoreCase) ? "SZ" : "SH";
            return $"{code}.{market}";
        }

        if (stockCode.All(char.IsDigit))
        {
            string digits = stockCode;
            string suffix = GetExchangeSuffix(digits);
            return $"{digits}.{suffix}";
        }

        return stockCode.ToUpper();
    }

    /// <summary>
    /// 提取股票代码中的所有数字字符
    /// </summary>
    private static string ExtractDigits(string stockCode)
    {
        return new string(stockCode.Where(char.IsDigit).ToArray());
    }

    /// <summary>
    /// 根据股票代码数字获取交易所前缀（SH/SZ）
    /// 上海证券交易所（SH）：60、688、900 开头
    /// 深圳证券交易所（SZ）：00、002、003、300、301、399 开头
    /// </summary>
    private static string GetExchangePrefix(string digits)
    {
        // 上海证券交易所：60开头（主板）、688开头（科创板）、900开头（B股）
        if (digits.StartsWith("60") ||
            digits.StartsWith("688") ||
            digits.StartsWith("900"))
            return "SH";

        // 深圳证券交易所：其他所有情况
        return "SZ";
    }

    /// <summary>
    /// 根据股票代码数字获取交易所后缀（SH/SZ）
    /// 上海证券交易所（SH）：600、601、603、605、688、900 开头
    /// 深圳证券交易所（SZ）：000、001、002、003、300、301、399 开头
    /// 默认：未知代码默认为上海证券交易所
    /// </summary>
    private static string GetExchangeSuffix(string digits)
    {
        // 深圳证券交易所：000/001（主板）、002（中小板）、003（主板）、300/301（创业板）、399（指数）
        if (digits.StartsWith("000") || digits.StartsWith("001") ||
            digits.StartsWith("002") || digits.StartsWith("003") ||
            digits.StartsWith("300") || digits.StartsWith("301") ||
            digits.StartsWith("399"))
        {
            return "SZ";
        }

        // 上海证券交易所：600/601/603/605（主板）、688（科创板）、900（B股）
        if (digits.StartsWith("600") || digits.StartsWith("601") ||
            digits.StartsWith("603") || digits.StartsWith("605") ||
            digits.StartsWith("688") || digits.StartsWith("900"))
        {
            return "SH";
        }

        // 默认返回上海交易所（保持与原有代码行为一致）
        return "SH";
    }
}
