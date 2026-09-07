using System.Net.Http.Json;
using MarketAssistant.DataProviders.Web3;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.DataProviders.Web3;

/// <summary>
/// DexScreener 客户端：多链 DEX 行情（热门代币搜索、交易对流动性/涨跌幅）。
/// 免费公开接口，无需 API Key；文档 https://docs.dexscreener.com/api/reference。
/// </summary>
public sealed class DexScreenerClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DexScreenerClient> _logger;

    public DexScreenerClient(IHttpClientFactory httpClientFactory, ILogger<DexScreenerClient> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 按关键字搜索 DEX 交易对（代币符号/名称/合约地址均可）。
    /// </summary>
    /// <param name="query">搜索关键字，如 "PEPE" 或合约地址</param>
    public async Task<List<DexScreenerPair>> SearchPairsAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new FriendlyException("DexScreener 搜索关键字不能为空");

        var url = $"latest/dex/search?q={Uri.EscapeDataString(query.Trim())}";
        _logger.LogDebug("调用 DexScreener 搜索: {Query}", query);

        try
        {
            using var httpClient = _httpClientFactory.CreateClient("DexScreener");
            var response = await httpClient.GetFromJsonAsync<DexScreenerSearchResponse>(url, cancellationToken);
            return response?.Pairs ?? [];
        }
        catch (HttpRequestException ex) when (ex.HttpRequestError == HttpRequestError.SecureConnectionError)
        {
            _logger.LogError(ex, "DexScreener API TLS 连接被重置: {Query}", query);
            throw new FriendlyException(
                "链上行情接口 TLS 连接被重置，多为当前网络对该域名的干扰：请在代理软件的分流规则中将 api.dexscreener.com 设为走代理线路后重试", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "调用 DexScreener 搜索失败: {Query}", query);
            throw new FriendlyException($"搜索链上交易对失败: {query}，请检查网络连接", ex);
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "解析 DexScreener 搜索结果失败: {Query}", query);
            throw new FriendlyException($"解析链上交易对数据失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 获取指定代币合约在指定链上的所有 DEX 交易对。链 ID 经 ChainRegistry 归一化为链全名。
    /// </summary>
    /// <param name="chainId">链 ID，支持规范名（ethereum/bsc/solana 等）及数字、别名等常见写法</param>
    /// <param name="tokenAddress">代币合约地址</param>
    public async Task<List<DexScreenerPair>> GetTokenPairsAsync(string chainId, string tokenAddress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tokenAddress))
            throw new FriendlyException("获取链上交易对需要指定代币合约地址");

        var canonicalChain = ChainRegistry.Normalize(chainId);
        var url = $"token-pairs/v1/{Uri.EscapeDataString(canonicalChain)}/{tokenAddress.Trim()}";
        _logger.LogDebug("调用 DexScreener 交易对查询: {ChainId} {TokenAddress}", chainId, tokenAddress);

        try
        {
            using var httpClient = _httpClientFactory.CreateClient("DexScreener");
            var pairs = await httpClient.GetFromJsonAsync<List<DexScreenerPair>>(url, cancellationToken);
            return pairs ?? [];
        }
        catch (HttpRequestException ex) when (ex.HttpRequestError == HttpRequestError.SecureConnectionError)
        {
            _logger.LogError(ex, "DexScreener API TLS 连接被重置: {ChainId} {TokenAddress}", chainId, tokenAddress);
            throw new FriendlyException(
                "链上行情接口 TLS 连接被重置，多为当前网络对该域名的干扰：请在代理软件的分流规则中将 api.dexscreener.com 设为走代理线路后重试", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "调用 DexScreener 交易对查询失败: {ChainId} {TokenAddress}", chainId, tokenAddress);
            throw new FriendlyException($"获取链上交易对失败，请检查链 ID 与合约地址是否正确", ex);
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "解析 DexScreener 交易对数据失败: {ChainId} {TokenAddress}", chainId, tokenAddress);
            throw new FriendlyException($"解析链上交易对数据失败: {ex.Message}", ex);
        }
    }
}