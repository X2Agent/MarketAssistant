using System.Text;

namespace MarketAssistant.DataProviders.AShare;

/// <summary>
/// 新浪 hq 接口的共享传输细节：GBK 编码注册、GBK 文本拉取与引号 payload 提取。
/// </summary>
/// <remarks>
/// <para>
/// 新浪接口返回 GBK 编码文本，而 .NET 默认编码表不含 GBK，必须先注册
/// <see cref="CodePagesEncodingProvider"/>。此前三个客户端（个股行情 / 指数行情 / 资金流）
/// 各自在静态构造函数中注册一遍，并各自重复
/// 「CreateClient → GetStreamAsync → StreamReader(gbk) → ReadToEnd」序列。
/// </para>
/// <para>
/// 收敛到此处的动机：注册点散落三处时，新增第四个新浪客户端很容易忘记注册，
/// 而在未注册的进程里 <c>Encoding.GetEncoding("gbk")</c> 直接抛
/// <see cref="ArgumentException"/>——故障点与根因分离，排查成本高。
/// </para>
/// </remarks>
internal static class SinaHqText
{
    /// <summary>GBK 解码器。首次接触本类型时完成编码注册（幂等），之后复用同一实例。</summary>
    private static readonly Encoding GbkEncoding = RegisterGbkEncoding();

    /// <summary>
    /// 以 GBK 解码拉取新浪接口的原始文本。
    /// </summary>
    /// <param name="httpClientFactory">HttpClient 工厂。</param>
    /// <param name="httpClientName">命名 HttpClient 名称（各客户端使用不同的命名客户端）。</param>
    /// <param name="path">请求路径（含查询串）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task<string> GetTextAsync(
        IHttpClientFactory httpClientFactory,
        string httpClientName,
        string path,
        CancellationToken cancellationToken)
    {
        using var httpClient = httpClientFactory.CreateClient(httpClientName);
        using var stream = await httpClient.GetStreamAsync(path, cancellationToken);
        using var reader = new StreamReader(stream, GbkEncoding);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    /// <summary>
    /// 提取 <c>var hq_str_sh600519="名称,今开,...";</c> 行中首末引号间的 payload 并按逗号分段。
    /// </summary>
    /// <returns>字段数组；引号缺失或空 payload（响应形态已变）时返回 <c>null</c>，由调用方按「无数据」降级。</returns>
    public static string[]? ExtractQuotedFields(string line)
    {
        var open = line.IndexOf('"');
        var close = line.LastIndexOf('"');
        if (open < 0 || close <= open)
        {
            return null;
        }

        return line[(open + 1)..close].Split(',');
    }

    private static Encoding RegisterGbkEncoding()
    {
        // RegisterProvider 幂等：重复调用只是把同一 provider 实例放回编码表，无副作用。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("gbk");
    }
}
