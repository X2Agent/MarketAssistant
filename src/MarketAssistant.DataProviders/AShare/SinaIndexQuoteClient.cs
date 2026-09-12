using System.Globalization;

namespace MarketAssistant.DataProviders.AShare;

/// <summary>
/// 新浪指数简版行情（<c>hq.sinajs.cn/list=s_*</c>）单条解析结果。
/// </summary>
/// <param name="Name">指数名称（如"上证指数"）。</param>
/// <param name="Point">最新点位。</param>
/// <param name="ChangePercent">涨跌幅（百分数值，如 -3.97 表示 -3.97%）。</param>
public sealed record SinaIndexQuote(
    string Name,
    decimal Point,
    decimal ChangePercent);

/// <summary>
/// 新浪指数行情客户端：一次请求拉取 A 股三大指数（上证/深证成指/创业板指）。
/// 仅负责 HTTP 访问与 <c>var hq_str_*</c> 文本解析（GBK 解码），不承载业务映射逻辑。
/// </summary>
public sealed class SinaIndexQuoteClient
{
    /// <summary>三大指数代码：上证指数、深证成指、创业板指。</summary>
    private const string IndexList = "s_sh000001,s_sz399001,s_sz399006";

    private const string QueryPath = "/list=" + IndexList;

    private readonly IHttpClientFactory _httpClientFactory;

    static SinaIndexQuoteClient()
    {
        // 新浪 hq 接口返回 GBK 编码，.NET 默认不支持，需注册 CodePagesEncodingProvider（幂等）。
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    public SinaIndexQuoteClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    /// <summary>
    /// 拉取三大指数行情。整体失败或无数据时返回空列表（由上层顶栏降级隐藏）。
    /// </summary>
    public async Task<List<SinaIndexQuote>> GetIndexesAsync(CancellationToken cancellationToken = default)
    {
        using var httpClient = _httpClientFactory.CreateClient("SinaHq");
        using var stream = await httpClient.GetStreamAsync(QueryPath, cancellationToken);
        using var reader = new StreamReader(stream, System.Text.Encoding.GetEncoding("gbk"));
        var text = await reader.ReadToEndAsync(cancellationToken);

        var result = new List<SinaIndexQuote>();
        // 每行形如：var hq_str_s_sh000001="上证指数,3094.668,-128.073,-3.97,264837,31490343";
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var quote = ParseLine(line);
            if (quote is not null)
                result.Add(quote);
        }

        return result;
    }

    private static SinaIndexQuote? ParseLine(string line)
    {
        var open = line.IndexOf('"');
        var close = line.LastIndexOf('"');
        if (open < 0 || close <= open)
            return null;

        var payload = line[(open + 1)..close];
        var fields = payload.Split(',');
        // 需要 名称,点位,涨跌额,涨跌幅 至少 4 段
        if (fields.Length < 4)
            return null;

        var name = fields[0].Trim();
        if (name.Length == 0)
            return null;

        if (!decimal.TryParse(fields[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var point))
            return null;
        decimal.TryParse(fields[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var changePercent);

        return new SinaIndexQuote(name, point, changePercent);
    }
}
