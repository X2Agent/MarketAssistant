using MarketAssistant.Applications.Assets.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.History;

/// <summary>
/// 虚拟币资产历史记录服务实现
/// </summary>
public class CryptoHistoryService : AssetHistoryServiceBase
{
    private readonly ILogger<CryptoHistoryService> _logger;

    protected override string PreferenceKey => "RecentAssets_Crypto";

    public CryptoHistoryService(ILogger<CryptoHistoryService> logger)
        : base(logger)
    {
        _logger = logger;
    }

    protected override void LogHistoryAdded(AssetItem asset)
    {
        _logger.LogInformation("已添加虚拟币到历史记录: {Code}", asset.Code);
    }

    protected override void LogHistoryCleared()
    {
        _logger.LogInformation("已清空虚拟币历史记录");
    }
}
