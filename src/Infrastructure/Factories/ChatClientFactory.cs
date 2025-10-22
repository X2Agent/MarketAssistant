using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

namespace MarketAssistant.Infrastructure.Factories;

/// <summary>
/// ChatClient 工厂接口
/// 负责创建和管理底层的 IChatClient 实例
/// </summary>
public interface IChatClientFactory
{
    /// <summary>
    /// 创建配置好的 ChatClient 实例
    /// </summary>
    IChatClient CreateClient();
}

/// <summary>
/// ChatClient 工厂实现
/// 创建和缓存底层的 OpenAI ChatClient
/// </summary>
public class ChatClientFactory : IChatClientFactory
{
    private readonly IUserSettingService _userSettingService;
    private readonly object _lock = new();
    private IChatClient? _cachedClient;
    private string? _lastError;

    public ChatClientFactory(IUserSettingService userSettingService)
    {
        _userSettingService = userSettingService;
    }

    public IChatClient CreateClient()
    {
        lock (_lock)
        {
            // 返回缓存的客户端
            if (_cachedClient != null)
                return _cachedClient;

            // 如果之前创建失败，返回缓存的错误
            if (!string.IsNullOrEmpty(_lastError))
                throw new FriendlyException(_lastError);

            try
            {
                var userSetting = _userSettingService.CurrentSetting;

                if (string.IsNullOrWhiteSpace(userSetting.ModelId))
                    throw new FriendlyException("AI 功能未配置:请先在设置页面选择 AI 模型");
                if (string.IsNullOrWhiteSpace(userSetting.ApiKey))
                    throw new FriendlyException("AI 功能未配置：请先在设置页面配置 API Key");
                if (string.IsNullOrWhiteSpace(userSetting.Endpoint))
                    throw new FriendlyException("AI 功能未配置：请先在设置页面配置 API 端点");

                var openAIClient = new OpenAIClient(
                    new ApiKeyCredential(userSetting.ApiKey),
                    new OpenAIClientOptions
                    {
                        Endpoint = new Uri(userSetting.Endpoint)
                    }
                );

                _cachedClient = openAIClient.GetChatClient(userSetting.ModelId).AsIChatClient();
                return _cachedClient;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                throw new FriendlyException(_lastError);
            }
        }
    }
}
