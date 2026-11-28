using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.DataProviders.Web3;

/// <summary>
/// GoPlus Security 客户端：代币安全审计与蜜罐检测（EVM 主端点与 Solana 专用端点，两者返回结构不同）。
/// API 域名为 api.gopluslabs.io；链 ID 词汇统一经 ChainRegistry 归一化。
/// </summary>
public sealed class GoPlusSecurityClient(IHttpClientFactory httpClientFactory, ILogger<GoPlusSecurityClient> logger)
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ILogger<GoPlusSecurityClient> _logger = logger;

    /// <summary>可选 API Key，由上层（如用户设置服务）在运行时注入。</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// 查询 EVM 链代币安全审计结果（含蜜罐检测、税率、增发/黑名单/暂停交易等风险项）。
    /// Solana 请使用 <see cref="GetSolanaTokenSecurityAsync"/>。
    /// </summary>
    /// <param name="chainId">链 ID，支持规范名（ethereum/bsc 等）及数字、别名等常见写法</param>
    /// <param name="tokenAddress">代币合约地址</param>
    public async Task<GoPlusTokenSecurity?> GetTokenSecurityAsync(string chainId, string tokenAddress, CancellationToken cancellationToken = default)
    {
        var address = NormalizeAddress(tokenAddress);
        var goPlusChainId = ResolveGoPlusChainId(ChainRegistry.Normalize(chainId));
        return await QuerySecurityAsync<GoPlusTokenSecurity>(
            $"api/v1/token_security/{goPlusChainId}?contract_addresses={Uri.EscapeDataString(address)}",
            address,
            cancellationToken);
    }

    /// <summary>
    /// 查询 Solana 代币安全审计结果（专用端点：增发/冻结/关闭等权限项、转账钩子、恶意地址标记）。
    /// </summary>
    /// <param name="tokenAddress">代币 mint 地址（保留大小写）</param>
    public async Task<GoPlusSolanaTokenSecurity?> GetSolanaTokenSecurityAsync(string tokenAddress, CancellationToken cancellationToken = default)
    {
        var address = NormalizeAddress(tokenAddress);
        return await QuerySecurityAsync<GoPlusSolanaTokenSecurity>(
            $"api/v1/solana/token_security?contract_addresses={Uri.EscapeDataString(address)}",
            address,
            cancellationToken);
    }

    private static string NormalizeAddress(string tokenAddress)
    {
        var address = tokenAddress.Trim();
        return address.Length > 0 ? address : throw new FriendlyException("代币安全审计需要提供合约地址");
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
            $"GoPlus 暂不支持该链: {canonical}，支持的主要链：ethereum、bsc、polygon、arbitrum、optimism、avalanche、base（也可直接使用数字链 ID；Solana 走专用端点）");
    }

    /// <summary>
    /// 执行 GET 请求并解析 GoPlus 包装响应（code 1 = 成功），取出目标地址对应的审计数据；未命中返回 null。
    /// GoPlus 返回的 result 键大小写不保证与入参一致，故忽略大小写匹配。
    /// </summary>
    private async Task<TSecurity?> QuerySecurityAsync<TSecurity>(string url, string address, CancellationToken cancellationToken) where TSecurity : class
    {
        _logger.LogDebug("调用 GoPlus API: {Url}", url);
        try
        {
            using var httpClient = _httpClientFactory.CreateClient("GoPlus");
            if (!string.IsNullOrWhiteSpace(ApiKey))
                httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);

            var response = await httpClient.GetFromJsonAsync<GoPlusResponse<TSecurity>>(url, cancellationToken);

            if (response is { Code: not 1 })
                throw new FriendlyException($"GoPlus 安全审计返回错误: {response.Message ?? response.Code.ToString()}");

            return response?.Result?.FirstOrDefault(pair =>
                string.Equals(pair.Key, address, StringComparison.OrdinalIgnoreCase)).Value;
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
