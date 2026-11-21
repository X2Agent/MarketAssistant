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
        if ((normalized.StartsWith("SH", StringComparison.Ordinal) || normalized.StartsWith("SZ", StringComparison.Ordinal)) &&
             normalized.Length > 2 &&
             normalized.Skip(2).All(char.IsDigit))
        {
            return normalized.ToLowerInvariant();
        }

        string digits = ToDigitsOnly(stockCode);
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
        if (cleanCode.StartsWith("SH", StringComparison.Ordinal) || cleanCode.StartsWith("SZ", StringComparison.Ordinal))
        {
            string code = cleanCode.Substring(2);
            string market = cleanCode.StartsWith("SZ", StringComparison.Ordinal) ? "SZ" : "SH";
            // 确保剩余部分是纯数字才转换
            if (code.All(char.IsDigit))
            {
                return $"{code}.{market}";
            }
        }

        // 3. 处理 600519SH / 000001SZ 后缀格式
        if (cleanCode.EndsWith("SH", StringComparison.Ordinal) || cleanCode.EndsWith("SZ", StringComparison.Ordinal))
        {
            // 长度检查，避免 SH / SZ 本身
            if (cleanCode.Length > 2)
            {
                string code = cleanCode.Substring(0, cleanCode.Length - 2);
                string market = cleanCode.EndsWith("SZ", StringComparison.Ordinal) ? "SZ" : "SH";
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
    /// 提取股票代码中的所有数字字符（如 sh600519 → 600519；无数字返回空串）。
    /// 各 A 股工具的"纯数字代码"归一化统一入口，不要在调用点手写 Where(char.IsDigit)。
    /// </summary>
    public static string ToDigitsOnly(string stockCode)
    {
        return new string([.. stockCode.Where(char.IsDigit)]);
    }

    /// <summary>
    /// 判定代码是否属于基金 / ETF / LOF 代码段（沪市 5 开头、深市 15/16 开头）。
    /// 这类标的智兔 K 线接口不覆盖（实测 503 "redis history miss" 或 404），
    /// 调用方据此优先走新浪数据源，省掉一次必然失败的请求与其退避等待。
    /// 可转债（11/12 开头）不在此列：它们是债券，语义与基金不同，仍按正常路径取数。
    /// </summary>
    public static bool IsFundOrEtf(string? stockCode)
    {
        var digits = ToDigitsOnly(stockCode ?? string.Empty);

        // A 股代码恒为 6 位；位数不符说明不是标准 A 股代码，不做猜测
        if (digits.Length != 6)
            return false;

        return digits[0] == '5' ||
               digits.StartsWith("15", StringComparison.Ordinal) ||
               digits.StartsWith("16", StringComparison.Ordinal);
    }

    /// <summary>
    /// 已知交易所前缀（沪深京），统一的大写标识编码。大小写不敏感匹配，输出恒为大写。
    /// </summary>
    private static readonly string[] KnownExchangePrefixes = ["SH", "SZ", "BJ"];

    /// <summary>
    /// 拆分交易所前缀（如 <c>SH600519</c> / <c>sh600519</c>）为（交易所, 代码）：
    /// 识别到已知前缀时交易所返回大写前缀、代码返回剥离前缀后的部分；
    /// 未识别时交易所返回空串、代码返回原值——由调用方按各自约定处置，
    /// <b>不做「未知默认上海」的猜测</b>（猜错等于把标的标到错误的交易所，
    /// 进而路由到错误市场的数据源与下单通道）。
    /// </summary>
    /// <remarks>
    /// 本方法是「带前缀符号 → 市场 + 代码」的统一入口；
    /// 裸数字代码的交易所推断走 <see cref="ResolveExchange"/>（经各格式转换方法间接复用）。
    /// 两者互补：前者解析显式标识，后者按代码段规则推断。
    /// </remarks>
    public static (string Market, string Code) SplitExchangePrefix(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return (string.Empty, string.Empty);

        var trimmed = symbol.Trim();
        if (trimmed.Length <= 2)
            return (string.Empty, trimmed);

        var prefix = trimmed[..2];
        foreach (var known in KnownExchangePrefixes)
        {
            if (string.Equals(prefix, known, StringComparison.OrdinalIgnoreCase))
            {
                return (known, trimmed[2..]);
            }
        }

        return (string.Empty, trimmed);
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
        if (digits.StartsWith('8') || digits.StartsWith("43", StringComparison.Ordinal) || digits.StartsWith("92", StringComparison.Ordinal))
            return "BJ";

        // 深圳证券交易所：0/3 开头（主板/创业板）、12 开头（可转债）、15/16 开头（ETF/LOF）
        if (digits.StartsWith('0') || digits.StartsWith('3') ||
            digits.StartsWith("12", StringComparison.Ordinal) || digits.StartsWith("15", StringComparison.Ordinal) || digits.StartsWith("16", StringComparison.Ordinal))
            return "SZ";

        // 其余默认上海证券交易所：6 开头（主板/科创板）、5 开头（ETF/LOF）、11 开头（可转债）、9 开头（B股）
        return "SH";
    }
}
