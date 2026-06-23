using MarketAssistant.Services.Settings;

namespace MarketAssistant.Infrastructure.Http;

/// <summary>
/// CoinGecko API Key 注入处理器
/// 从用户设置读取 CoinGeckoApiKey，按密钥类型添加对应请求头：
/// - 以 "CG-Pro-" 开头：添加 x-cg-pro-api-key（付费版）
/// - 其他：添加 x-cg-demo-api-key（免费 Demo 版）
/// - 为空：不添加（兼容旧版公共端点，但 /coins/markets 可能返回 401）
/// </summary>
public sealed class CoinGeckoApiKeyHandler : DelegatingHandler
{
    private readonly IUserSettingService _userSettingService;

    public CoinGeckoApiKeyHandler(IUserSettingService userSettingService)
    {
        _userSettingService = userSettingService ?? throw new ArgumentNullException(nameof(userSettingService));
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var apiKey = _userSettingService.CurrentSetting.CoinGeckoApiKey;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var headerName = apiKey.StartsWith("CG-Pro-", StringComparison.OrdinalIgnoreCase)
                ? "x-cg-pro-api-key"
                : "x-cg-demo-api-key";
            request.Headers.TryAddWithoutValidation(headerName, apiKey.Trim());
        }
        return base.SendAsync(request, cancellationToken);
    }
}
