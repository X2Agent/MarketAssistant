namespace MarketAssistant.Infrastructure.Core;

/// <summary>
/// 股票代码格式转换工具类
/// </summary>
public static class StockSymbolConverter
{
    /// <summary>
    /// 将股票代码转换为财联社格式（如 sh600000、sz000001，均为小写）
    /// </summary>
    public static string ToClsFormat(string stockCode)
    {
        if (string.IsNullOrWhiteSpace(stockCode)) return string.Empty;

        // 预处理：仅规范化大小写，保留数字和字母供后续判断
        string normalized = stockCode.ToUpperInvariant().Replace("/", "").Replace("-", "").Replace(" ", "");

        // 如果已经是 SHxxxxxx 或 SZxxxxxx 格式（且后续全是数字）
        if ((normalized.StartsWith("SH") || normalized.StartsWith("SZ")) &&
             normalized.Length > 2 &&
             normalized.Skip(2).All(char.IsDigit))
        {
            return normalized.ToLowerInvariant();
        }

        string digits = ExtractDigits(stockCode);
        if (string.IsNullOrEmpty(digits)) return stockCode; // 无法提取数字则返回原值

        string prefix = ResolveExchange(digits);
        return $"{prefix}{digits}".ToLowerInvariant();
    }

    /// <summary>
    /// 将股票代码转换为智图API格式（如 600000.SH、000001.SZ）
    /// </summary>
    public static string ToZhiTuFormat(string stockCode)
    {
        if (string.IsNullOrWhiteSpace(stockCode)) return string.Empty;

        // 移除常见分隔符
        string cleanCode = stockCode.Replace("/", "").Replace("-", "").Replace(" ", "").ToUpperInvariant();

        // 1. 处理如 600519.SH 的标准格式
        if (cleanCode.Contains('.'))
        {
            return cleanCode;
        }

        // 2. 处理 SH600519 / SZ000001 前缀格式
        if (cleanCode.StartsWith("SH") || cleanCode.StartsWith("SZ"))
        {
            string code = cleanCode.Substring(2);
            string market = cleanCode.StartsWith("SZ") ? "SZ" : "SH";
            // 确保剩余部分是纯数字才转换
            if (code.All(char.IsDigit))
            {
                return $"{code}.{market}";
            }
        }

        // 3. 处理 600519SH / 000001SZ 后缀格式
        if (cleanCode.EndsWith("SH") || cleanCode.EndsWith("SZ"))
        {
            // 长度检查，避免 SH / SZ 本身
            if (cleanCode.Length > 2)
            {
                string code = cleanCode.Substring(0, cleanCode.Length - 2);
                string market = cleanCode.EndsWith("SZ") ? "SZ" : "SH";
                if (code.All(char.IsDigit))
                {
                    return $"{code}.{market}";
                }
            }
        }

        // 4. 处理纯数字 600519
        if (cleanCode.All(char.IsDigit))
        {
            string digits = cleanCode;
            string suffix = ResolveExchange(digits);
            return $"{digits}.{suffix}";
        }

        // 5. 无法识别，返回大写原值
        return cleanCode;
    }

    /// <summary>
    /// 提取股票代码中的所有数字字符
    /// </summary>
    private static string ExtractDigits(string stockCode)
    {
        return new string(stockCode.Where(char.IsDigit).ToArray());
    }

    /// <summary>
    /// 根据代码数字统一解析所属交易所（单一判定入口，供各类格式转换复用）。
    /// 上海证券交易所（SH）：6 开头（主板/科创板）、5 开头（基金 ETF/LOF）、11 开头（可转债）、9 开头（B股）
    /// 深圳证券交易所（SZ）：0/3 开头（主板/创业板）、12 开头（可转债）、15/16 开头（基金 ETF）
    /// 北京证券交易所（BJ）：8 开头（北交所/新三板）、43/92 开头
    /// 默认：未知代码默认为上海证券交易所（保持与原有代码行为一致）
    /// </summary>
    private static string ResolveExchange(string digits)
    {
        // 北交所：8 开头（83/87/88 等）、43/92 开头
        if (digits.StartsWith("8") || digits.StartsWith("43") || digits.StartsWith("92"))
            return "BJ";

        // 深圳证券交易所：0/3 开头（主板/创业板）、12 开头（可转债）、15/16 开头（ETF/LOF）
        if (digits.StartsWith("0") || digits.StartsWith("3") ||
            digits.StartsWith("12") || digits.StartsWith("15") || digits.StartsWith("16"))
            return "SZ";

        // 其余默认上海证券交易所：6 开头（主板/科创板）、5 开头（ETF/LOF）、11 开头（可转债）、9 开头（B股）
        return "SH";
    }
}
