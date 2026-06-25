namespace MarketAssistant.Applications.Telegrams;

/// <summary>
/// 快讯服务接口，支持多市场实现
/// </summary>
public interface ITelegramService
{
    /// <summary>
    /// 获取实时快讯
    /// </summary>
    Task<List<Telegram>> GetTelegraphsAsync(CancellationToken cancellationToken = default);
}

