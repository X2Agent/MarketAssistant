using System.Globalization;

namespace MarketAssistant.DataProviders.AShare;

/// <summary>新浪盘口单档（数量单位：股）。</summary>
public sealed record SinaOrderBookLevel(decimal Price, decimal Quantity);

/// <summary>
/// 新浪个股行情单条解析结果（<c>hq.sinajs.cn/list=sh600519</c>，非 <c>s_</c> 简版）。
/// </summary>
/// <param name="Name">证券名称。</param>
/// <param name="Open">今开；字段缺失时为 null（领域层约定的「未提供」）。</param>
/// <param name="PreClose">昨收；字段缺失时为 null。</param>
/// <param name="CurrentPrice">最新价。</param>
/// <param name="High">最高；字段缺失时为 null。</param>
/// <param name="Low">最低；字段缺失时为 null。</param>
/// <param name="Volume">成交量（股）；字段缺失时为 null。</param>
/// <param name="Amount">成交额（元）；字段缺失时为 null。</param>
/// <param name="Bids">买一至买五（价格降序）。</param>
/// <param name="Asks">卖一至卖五（价格升序）。</param>
/// <remarks>
/// 该接口<b>不提供逐笔成交明细</b>：字段 26..29 实为卖四/卖五档位，30 起为日期与时间，
/// 早期版本据错误假设解析出的"逐笔"只是卖盘档位，已移除。需要逐笔的市场不应依赖本客户端。
/// </remarks>
public sealed record SinaStockQuote(
    string Name,
    decimal CurrentPrice,
    decimal? Open,
    decimal? PreClose,
    decimal? High,
    decimal? Low,
    decimal? Volume,
    decimal? Amount,
    IReadOnlyList<SinaOrderBookLevel> Bids,
    IReadOnlyList<SinaOrderBookLevel> Asks);

/// <summary>
/// 新浪个股行情客户端：拉取单只 A 股的五档盘口与开高低收等快照字段。
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
/// <para>
/// <b>字段布局（2026-10 实测抓包校准）</b>：0 名称 / 1 今开 / 2 昨收 / 3 最新 /
/// 4 最高 / 5 最低 / 6 竞买价 / 7 竞卖价 / 8 成交量(股) / 9 成交额(元) /
/// 10..19 买一~买五「量,价」对 / 20..29 卖一~卖五「量,价」对 / 30 日期 / 31 时间。
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
    private const int OpenIndex = 1;
    private const int PreCloseIndex = 2;
    private const int CurrentPriceIndex = 3;
    private const int HighIndex = 4;
    private const int LowIndex = 5;
    private const int VolumeIndex = 8;
    private const int AmountIndex = 9;

    /// <summary>买盘「量,价」对的起始下标（买一量）。</summary>
    private const int BidStartIndex = 10;

    /// <summary>卖盘「量,价」对的起始下标（卖一量）。</summary>
    private const int AskStartIndex = 20;

    /// <summary>卖五价下标，决定响应至少需要的字段段数。</summary>
    private const int LastAskPriceIndex = AskStartIndex + (Depth - 1) * 2 + 1;

    /// <summary>五档：买一~买五 / 卖一~卖五 各为「量,价」成对字段。</summary>
    private const int Depth = 5;

    private readonly IHttpClientFactory _httpClientFactory =
        httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));

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
            var text = await SinaHqText.GetTextAsync(
                _httpClientFactory, "SinaHq", $"/list={sinaCode}", cancellationToken);

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
    /// 解析 <c>var hq_str_sh600519="名称,今开,昨收,最新,最高,最低,竞买价,竞卖价,成交量,成交额,买一量,买一价,...,卖五量,卖五价,日期,时间";</c>。
    /// </summary>
    internal static SinaStockQuote? ParseResponse(string text)
    {
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(candidate => candidate.Contains("hq_str_", StringComparison.Ordinal));
        if (line is null)
        {
            return null;
        }

        var fields = SinaHqText.ExtractQuotedFields(line);
        if (fields is null)
        {
            return null;
        }

        // 卖五价位于下标 29，少于此说明响应形态已变，直接拒绝
        if (fields.Length <= LastAskPriceIndex)
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

        // 核心防线：五档必须自洽且贴近最新价，否则视为字段错位并放弃整条数据
        if (!IsPlausible(bids, asks, currentPrice))
        {
            return null;
        }

        return new SinaStockQuote(
            name,
            currentPrice,
            TryParseNullableDecimal(fields[OpenIndex]),
            TryParseNullableDecimal(fields[PreCloseIndex]),
            TryParseNullableDecimal(fields[HighIndex]),
            TryParseNullableDecimal(fields[LowIndex]),
            TryParseNullableDecimal(fields[VolumeIndex]),
            TryParseNullableDecimal(fields[AmountIndex]),
            bids,
            asks);
    }

    /// <summary>解析从 <paramref name="quantityStartIndex"/> 起的 depth 组「量,价」成对字段。</summary>
    private static List<SinaOrderBookLevel> ParseLevels(string[] fields, int quantityStartIndex, bool descending)
    {
        var levels = new List<SinaOrderBookLevel>(Depth);
        for (var i = 0; i < Depth; i++)
        {
            var quantityIndex = quantityStartIndex + i * 2;
            var priceIndex = quantityIndex + 1;
            if (priceIndex >= fields.Length)
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

    private static bool TryParseDecimal(string? value, out decimal result) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out result);

    /// <summary>快照字段缺失或不可解析时返回 null（「未提供」），绝不静默归零。</summary>
    private static decimal? TryParseNullableDecimal(string? value) =>
        TryParseDecimal(value, out var result) && result > 0m ? result : null;
}
