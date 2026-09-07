using System.Text.Json;

namespace MarketAssistant.DataProviders.AShare;

/// <summary>
/// 智兔（zhituapi.com）A 股数据客户端。
/// 覆盖公司资料、财务报表、技术指标、K 线、资金流向等接口；
/// 泛型反序列化由本层统一执行（容错字符串数值/null/-- 占位），
/// 业务 DTO 类型仍归属上层契约模块。
/// </summary>
public sealed class ZhiTuMarketClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ZhiTuMarketClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    /// <summary>
    /// 获取财务报表类列表数据（<c>/hs/fin/{endpoint}/{code}</c>）。
    /// </summary>
    /// <typeparam name="T">上层业务 DTO 类型。</typeparam>
    public async Task<List<T>> GetFinancialListAsync<T>(
        string endpoint, string zhiTuCode, string token,
        string startDate, string endDate, CancellationToken cancellationToken = default)
    {
        var path = $"/hs/fin/{endpoint}/{zhiTuCode}?token={token}&st={startDate}&et={endDate}";
        return await GetListAsync<T>(path, cancellationToken);
    }

    /// <summary>
    /// 获取技术指标历史序列（<c>/hs/history/{indicator}/{code}/d/n?lt=30</c>）。
    /// </summary>
    /// <typeparam name="T">上层业务 DTO 类型。</typeparam>
    public async Task<List<T>> GetIndicatorListAsync<T>(
        string indicator, string zhiTuCode, string token, CancellationToken cancellationToken = default)
    {
        var path = $"/hs/history/{indicator}/{zhiTuCode}/d/n?token={token}&lt=30";
        return await GetListAsync<T>(path, cancellationToken);
    }

    /// <summary>
    /// 获取 K 线序列（<c>/hs/history/{code}/{interval}/n</c>）。
    /// </summary>
    /// <typeparam name="TBar">K 线条目 DTO 类型。</typeparam>
    public async Task<List<TBar>> GetKLineBarsAsync<TBar>(
        string zhiTuCode, string interval, string token,
        string startDate, string endDate, CancellationToken cancellationToken = default)
    {
        var path = $"/hs/history/{zhiTuCode}/{interval}/n?token={token}&st={startDate}&et={endDate}";
        return await GetListAsync<TBar>(path, cancellationToken);
    }

    /// <summary>
    /// 获取个股资金流向历史（<c>/hs/history/transaction/{code}?lt=20</c>）。
    /// </summary>
    /// <typeparam name="T">资金流向 DTO 类型。</typeparam>
    public async Task<List<T>> GetTransactionHistoryAsync<T>(
        string digitsCode, string token, CancellationToken cancellationToken = default)
    {
        var path = $"/hs/history/transaction/{digitsCode}?token={token}&lt=20";
        return await GetListAsync<T>(path, cancellationToken);
    }

    /// <summary>
    /// 获取公司基本资料（<c>/hs/gs/gsjj/{code}</c>）。
    /// </summary>
    /// <typeparam name="T">公司资料 DTO 类型。</typeparam>
    public async Task<T?> GetCompanyInfoAsync<T>(
        string digitsCode, string token, CancellationToken cancellationToken = default)
    {
        var path = $"/hs/gs/gsjj/{digitsCode}?token={token}";
        using var httpClient = _httpClientFactory.CreateClient("ZhiTu");
        var response = await httpClient.GetStringAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<T>(response, AShareJsonOptions.Instance);
    }

    /// <summary>
    /// 通用 GET + 容错反序列化为列表。供 K 线图表服务等需要自定义路径的调用方使用。
    /// </summary>
    public async Task<List<T>> GetListAsync<T>(
        string requestPathAndQuery, CancellationToken cancellationToken = default)
    {
        using var httpClient = _httpClientFactory.CreateClient("ZhiTu");
        using var response = await httpClient.GetAsync(requestPathAndQuery, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"智图API返回 {(int)response.StatusCode}，{(string.IsNullOrWhiteSpace(errorBody) ? "请稍后重试" : errorBody)}");
        }

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<List<T>>(responseContent, AShareJsonOptions.Instance) ?? [];
    }
}