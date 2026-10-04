using System.Globalization;

namespace MarketAssistant.DataProviders.AShare;

/// <summary>新浪盘口单档（数量单位：股）。</summary>
public sealed record SinaOrderBookLevel(decimal Price, decimal Quantity);

/// <summary>新浪逐笔成交（数量单位：股）。</summary>
public sealed record SinaTick(decimal Price, decimal Quantity);

/// <summary>
/// 新浪个股行情单条解析结果（<c>hq.sinajs.cn/list=sh600519</c>，非 <c>s_</c> 简版）。
/// </summary>
/// <param name="Name">证券名称。</param>
/// <param name="CurrentPrice">最新价。</param>
/// <param name="Bids">买一至买五（价格降序）。</param>
/// <param name="Asks">卖一至卖五（价格升序）。</param>
/// <param name="Ticks">逐笔成交（成交量单位：股；源不提供主动买卖方向）。</param>
public sealed record SinaStockQuote(
    string Name,
    decimal CurrentPrice,
    IReadOnlyList<SinaOrderBookLevel> Bids,
    IReadOnlyList<SinaOrderBookLevel> Asks,
    IReadOnlyList<SinaTick> Ticks);

/// <summary>
/// 新浪个股行情客户端：拉取单只 A 股的五档盘口与逐笔成交。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么要做防御性校验：</b>新浪该接口为非公开约定，返回的是无官方文档的
/// 逗号分隔文本，字段下标可能随接口调整而漂移。若解析器"想当然"地按下标取值，
/// 字段一旦错位就会产出<em>看似合理但完全错误</em>的盘口价格——
/// 对盯盘用户而言，展示错误价格比不展示危害更大。
/// </para>
/// <para>
/// 因此本客户端对解析结果做交叉校验（买盘降序、卖盘升序、无盘口倒挂、
/// 价格贴近最新价），任一条不满足即整体返回 <c>null</c>，由上层展示"数据不可用"。
/// 这保证"接口变了而我不知情"降级为"没有数据"，而不是"错误数据"。
/// </para>
/// </remarks>
public sealed class SinaStockQuoteClient(IHttpClientFactory httpClientFactory)
{
    /// <summary>
    /// 盘口档位相对最新价的允许最大偏离。A 股日内涨跌幅上限为 10%（创业板/科创板 20%），
    /// 取 25% 作为宽松上界，仅用于识别"字段错位"这类结构性错误，不用于业务判断。
    /// </summary>
    private const decimal MaxRelativeDeviation = 0.25m;

    private const int NameIndex = 0;
    private const int CurrentPriceIndex = 3;
    private const int BidStartIndex = 6;
    private const int AskStartIndex = 16;
    private const int TicksIndex = 26;

    /// <summary>五档：买一~买五 / 卖一~卖五 各为「价格,数量」成对字段。</summary>
    private const int Depth = 5;

    private readonly IHttpClientFactory _httpClientFactory =
        httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));

    static SinaStockQuoteClient()
    {
        // 新浪 hq 接口返回 GBK 编码，.NET 默认不支持，需注册 CodePagesEncodingProvider（幂等）。
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// 拉取单只个股的五档盘口与逐笔成交。
    /// </summary>
    /// <param name="sinaCode">新浪格式代码（如 <c>sh600519</c>、<c>sz000001</c>）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>解析成功且通过校验时返回结果；代码非法、响应异常或校验不通过时返回 <c>null</c>。</returns>
    public async Task<SinaStockQuote?> GetStockQuoteAsync(string sinaCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sinaCode) || !IsSupportedCode(sinaCode))
        {
            return null;
        }

        try
        {
            using var httpClient = _httpClientFactory.CreateClient("SinaHq");
            using var stream = await httpClient.GetStreamAsync($"/list={sinaCode}", cancellationToken);
            using var reader = new StreamReader(stream, System.Text.Encoding.GetEncoding("gbk"));
            var text = await reader.ReadToEndAsync(cancellationToken);

            return ParseResponse(text);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // 数据源抖动不应冒泡到 UI：调用方按"无数据"降级即可
            return null;
        }
    }

    /// <summary>仅接受沪深京三市的新浪个股代码前缀，拒绝指数（s_/z_）等其它形态。</summary>
    private static bool IsSupportedCode(string sinaCode)
        => sinaCode.Length > 2 &&
           sinaCode[0] is 's' or 'b' &&
           sinaCode[1] is 'h' or 'z' or 'j' &&
           sinaCode.AsSpan(2).IndexOfAny("_.") < 0;

    /// <summary>
    /// 解析 <c>var hq_str_sh600519="名称,开盘,昨收,最新,最高,最低,买一,买一量,...,逐笔,日期,时间,...";</c>。
    /// </summary>
    internal static SinaStockQuote? ParseResponse(string text)
    {
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(candidate => candidate.Contains("hq_str_", StringComparison.Ordinal));
        if (line is null)
        {
            return null;
        }

        var open = line.IndexOf('"');
        var close = line.LastIndexOf('"');
        if (open < 0 || close <= open)
        {
            return null;
        }

        var fields = line[(open + 1)..close].Split(',');

        // 逐笔字段下标为 26，故至少需要 27 段；少于此说明响应形态已变，直接拒绝
        if (fields.Length <= TicksIndex)
        {
            return null;
        }

        var name = fields[NameIndex].Trim();
        if (name.Length == 0)
        {
            return null;
        }

        if (!TryParseDecimal(fields[CurrentPriceIndex], out var currentPrice) || currentPrice <= 0m)
        {
            return null;
        }

        var bids = ParseLevels(fields, BidStartIndex, descending: true);
        var asks = ParseLevels(fields, AskStartIndex, descending: false);
        var ticks = ParseTicks(JoinTailFields(fields));

        // 核心防线：五档必须自洽且贴近最新价，否则视为字段错位并放弃整条数据
        if (!IsPlausible(bids, asks, currentPrice))
        {
            return null;
        }

        return new SinaStockQuote(name, currentPrice, bids, asks, ticks);
    }

    /// <summary>解析从 <paramref name="startIndex"/> 起的 depth 组「价格,数量」成对字段。</summary>
    private static List<SinaOrderBookLevel> ParseLevels(string[] fields, int startIndex, bool descending)
    {
        var levels = new List<SinaOrderBookLevel>(Depth);
        for (var i = 0; i < Depth; i++)
        {
            var priceIndex = startIndex + i * 2;
            var quantityIndex = priceIndex + 1;
            if (quantityIndex >= fields.Length)
            {
                break;
            }

            // 挂单价为 0 表示该档无挂单（停牌/集合竞价等），跳过而非当作真实价格
            if (!TryParseDecimal(fields[priceIndex], out var price) || price <= 0m)
            {
                continue;
            }

            // 数量解析失败时保留 0：该档仍展示价格，但数量为 0 表示"未知/无挂单"，
            // 优于丢弃整档导致买一买二跳档。刻意忽略返回值（TryParse 已把失败置为 0）。
            _ = TryParseDecimal(fields[quantityIndex], out var quantity);
            if (quantity < 0m)
            {
                quantity = 0m;
            }

            levels.Add(new SinaOrderBookLevel(price, quantity));
        }

        return descending
            ? [.. levels.OrderByDescending(level => level.Price)]
            : [.. levels.OrderBy(level => level.Price)];
    }

    /// <summary>
    /// 交叉校验：买一不得高于卖一（否则盘口倒挂，多半是字段错位），
    /// 且各档价格需落在最新价的合理邻域内。
    /// </summary>
    private static bool IsPlausible(
        List<SinaOrderBookLevel> bids,
        List<SinaOrderBookLevel> asks,
        decimal currentPrice)
    {
        if (bids.Count == 0 && asks.Count == 0)
        {
            return false;
        }

        var lowerBound = currentPrice * (1m - MaxRelativeDeviation);
        var upperBound = currentPrice * (1m + MaxRelativeDeviation);

        foreach (var level in bids.Concat(asks))
        {
            if (level.Price < lowerBound || level.Price > upperBound)
            {
                return false;
            }
        }

        if (bids.Count > 0 && asks.Count > 0 && bids[0].Price > asks[0].Price)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 还原下标 26 之后的尾部文本。
    /// </summary>
    /// <remarks>
    /// <b>关键：</b>新浪的逐笔字段本身含逗号（如 <c>1800.00,100,1799.99,200</c>），
    /// 因此整段载荷按逗号切分后，下标 26 之后的每个元素并非"独立字段"，
    /// 而是被逐笔内容打散的片段。若直接读取 <c>fields[26]</c>，
    /// 只会拿到第一笔成交的价格，而丢失数量与其后所有成交。
    /// 这里必须把尾部重新拼接，逐笔解析交由 <see cref="ParseTicks"/> 按成对规则处理。
    /// </remarks>
    private static string JoinTailFields(string[] fields) =>
        string.Join(',', fields.Skip(TicksIndex));

    /// <summary>
    /// 解析逐笔成交段落。格式为连续的 <c>价格,数量</c> 对，其后紧跟日期与时间。
    /// </summary>
    /// <remarks>
    /// 该字段<b>不提供主动买卖方向</b>，故方向信息不可得，调用方不应据此推断内外盘。
    /// 遇到无法解析为数值的 token（如 <c>2026-10-04</c>）即判定为逐笔段落结束。
    /// </remarks>
    private static List<SinaTick> ParseTicks(string tail)
    {
        var ticks = new List<SinaTick>();
        if (string.IsNullOrWhiteSpace(tail))
        {
            return ticks;
        }

        var parts = tail.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i + 1 < parts.Length; i += 2)
        {
            // 奇数段为落单尾巴；非数值 token 表明已进入日期/时间区域，二者都应终止解析
            if (!TryParseDecimal(parts[i], out var price) || !TryParseDecimal(parts[i + 1], out var quantity))
            {
                break;
            }

            if (price > 0m && quantity > 0m)
            {
                ticks.Add(new SinaTick(price, quantity));
            }
        }

        return ticks;
    }

    private static bool TryParseDecimal(string? value, out decimal result) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out result);
}
