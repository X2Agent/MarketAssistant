using MarketAssistant.Agents.Plugins.Models;
using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Text.Json;

namespace MarketAssistant.Agents.Tools.AShare;

/// <summary>
/// A股市场情绪工具实现（从MarketSentimentTools迁移）
/// </summary>
public sealed class AShareSentimentTools : ISentimentDataTools
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IUserSettingService _userSettingService;

    public AShareSentimentTools(IHttpClientFactory httpClientFactory, IUserSettingService userSettingService)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _userSettingService = userSettingService ?? throw new ArgumentNullException(nameof(userSettingService));
    }

    [Description("根据股票代码获取资金流向数据")]
    public async Task<FundFlow> GetFundFlowAsync([Description("股票代码")] string assetSymbol)
    {
        // 迁移自MarketSentimentTools的实现
        assetSymbol = new string(assetSymbol.Where(char.IsDigit).ToArray());
        var token = _userSettingService.CurrentSetting.ZhiTuApiToken;
        var url = $"https://api.zhituapi.com/hs/zjlx/{assetSymbol}?token={token}";

        using var httpClient = _httpClientFactory.CreateClient();
        var response = await httpClient.GetStringAsync(url);
        var fundFlow = JsonSerializer.Deserialize<FundFlow>(response);

        return fundFlow ?? throw new Exception("GetFundFlowAsync返回数据为空");
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetFundFlowAsync);
    }
}






