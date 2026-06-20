using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Models.AShare;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Data;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.AShare;

/// <summary>
/// A股市场情绪工具实现
/// </summary>
public sealed class AShareSentimentTools : IShareSentimentTools
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IUserSettingService _userSettingService;
    private readonly ILogger<AShareSentimentTools> _logger;

    // 支持 API 返回的字符串数值自动转换为 float
    private static readonly JsonSerializerOptions SentimentJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new StringToDecimalConverter() }
    };

    public AShareSentimentTools(
        IHttpClientFactory httpClientFactory,
        IUserSettingService userSettingService,
        ILogger<AShareSentimentTools> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _userSettingService = userSettingService ?? throw new ArgumentNullException(nameof(userSettingService));
        _logger = logger;
    }

    [Description("根据股票代码获取资金流向数据")]
    public async Task<FundFlow> GetFundFlowAsync([Description("股票代码")] string assetSymbol)
    {
        try
        {
            assetSymbol = new string(assetSymbol.Where(char.IsDigit).ToArray());
            var token = _userSettingService.CurrentSetting.ZhiTuApiToken;
            var url = $"/hs/zjlx/{assetSymbol}?token={token}";

            using var httpClient = _httpClientFactory.CreateClient("ZhiTu");
            var response = await httpClient.GetStringAsync(url);
            var fundFlow = JsonSerializer.Deserialize<FundFlow>(response, SentimentJsonOptions);

            return fundFlow ?? throw new FriendlyException($"获取资金流向数据为空: {assetSymbol}");
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "获取资金流向失败: {Symbol}", assetSymbol);
            throw new FriendlyException($"获取资金流向数据时发生错误: {ex.Message}", ex);
        }
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetFundFlowAsync);
    }
}
