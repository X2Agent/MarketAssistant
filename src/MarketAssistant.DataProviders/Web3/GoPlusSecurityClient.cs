using System.Net.Http.Json;
using MarketAssistant.DataProviders.Web3;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.DataProviders.Web3;

/// <summary>
/// GoPlus Security 客户端：代币安全审计与蜜罐检测（EVM 多链 + Solana 专用端点）。
/// API 域名为 api.gopluslabs.io；链 ID 词汇统一经 ChainRegistry 归一化。
/// </summary>
public sealed class GoPlusSecurityClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GoPlusSecurityClient> _logger;

    /// <summary>可选 API Key，由上层（如用户设置服务）在运行时注入。</summary>
    public string? ApiKey { get; set; }

    public GoPlusSecurityClient(IHttpClientFactory httpClientFactory, ILogger<GoPlusSecurityClient> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 查询代币安全审计结果（含蜜罐检测、税率、增发/黑名单/暂停交易等风险项）。
    /// Solana 代币自动路由到 GoPlus 专用 Solana 端点。
    /// </summary>
    /// <param name="chainId">链 ID，支持规范名（ethereum/bsc/solana 等）及数字、别名等常见写法</param>
    /// <param name="tokenAddress">代币合约地址（Solana 为 mint 地址，保留大小写）</param>
    public async Task<GoPlusTokenSecurity?> GetTokenSecurityAsync(string chainId, string tokenAddress, CancellationToken cancellationToken = default)
    {
        var canonical = ChainRegistry.Normalize(chainId);
        var normalAddress = tokenAddress?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalAddress))
            throw new FriendlyException("代币安全审计需要提供合约地址");

        var isSolana = canonical == "solana";
        var goPlusChainId = isSolana ? null : ResolveGoPlusChainId(canonical);
        var url = isSolana
            ? $"api/v1/solana/token_security?contract_addresses={Uri.EscapeDataString(normalAddress)}"
            : $"api/v1/token_security/{goPlusChainId}?contract_addresses={Uri.EscapeDataString(normalAddress)}";

        var data = await ExecuteAsync<GoPlusSecurityResponse>(url, cancellationToken);
        if (data?.Result is not { Count: > 0 })
            return null;

        var lookupKey = isSolana ? normalAddress : normalAddress.ToLowerInvariant();
        if (data.Result.TryGetValue(lookupKey, out var security))
            return security;

        // GoPlus 返回的 result 键大小写不保证与入参一致，未精确命中时忽略大小写回退
        return data.Result.FirstOrDefault(kv =>
            string.Equals(kv.Key, normalAddress, StringComparison.OrdinalIgnoreCase)).Value
            ?? data.Result.Values.First();
    }

    /// <summary>
    /// 解析 GoPlus 主端点的数字链 ID：注册链经 ChainRegistry 转换；未注册的数字链 ID 透传，
    /// 其余未收录链明确报错，避免拼出缺链 ID 的 URL。
    /// </summary>
    private static string ResolveGoPlusChainId(string canonical)
    {
        var registered = ChainRegistry.ToGoPlusChainId(canonical);
        if (!string.IsNullOrEmpty(registered))
            return registered;

        if (canonical.All(char.IsAsciiDigit))
            return canonical;

        throw new FriendlyException(
            $"GoPlus 暂不支持该链: {canonical}，支持的主要链：ethereum、bsc、polygon、arbitrum、optimism、avalanche、base（Solana 自动路由专用端点，也可直接使用数字链 ID）");
    }

    /// <summary>
    /// 统一执行 GET 请求并解析 GoPlus 包装响应（code 1 = 成功）。
    /// </summary>
    private async Task<T?> ExecuteAsync<T>(string url, CancellationToken cancellationToken) where T : class
    {
        _logger.LogDebug("调用 GoPlus API: {Url}", url);
        try
        {
            using var httpClient = _httpClientFactory.CreateClient("GoPlus");
            if (!string.IsNullOrWhiteSpace(ApiKey))
                httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);

            var response = await httpClient.GetFromJsonAsync<T>(url, cancellationToken);

            if (response is GoPlusSecurityResponse sec && sec.Code != 1)
                throw new FriendlyException($"GoPlus 安全审计返回错误: {sec.Message ?? sec.Code.ToString()}");

            return response;
        }
        catch (HttpRequestException ex) when (ex.HttpRequestError == HttpRequestError.SecureConnectionError)
        {
            _logger.LogError(ex, "GoPlus API TLS 连接被重置: {Url}", url);
            throw new FriendlyException(
                "GoPlus 接口 TLS 连接被重置，请检查网络/代理对 api.gopluslabs.io 的连通性后重试", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "调用 GoPlus API 失败: {Url}", url);
            throw new FriendlyException("链上安全数据获取失败，请检查网络连接", ex);
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "解析 GoPlus API 响应失败: {Url}", url);
            throw new FriendlyException($"解析链上安全数据失败: {ex.Message}", ex);
        }
    }
}