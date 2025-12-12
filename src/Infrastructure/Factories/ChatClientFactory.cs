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

    // 缓存用于创建客户端的配置，以便检测变更
    private string? _cachedModelId;
    private string? _cachedEndpoint;
    private string? _cachedApiKey;

    public ChatClientFactory(IUserSettingService userSettingService)
    {
        _userSettingService = userSettingService;
    }

    public IChatClient CreateClient()
    {
        lock (_lock)
        {
            var userSetting = _userSettingService.CurrentSetting;
            var modelId = userSetting.ModelId;
            var apiKey = userSetting.ApiKey;
            var endpoint = userSetting.Endpoint;

            // 检查配置是否变更，如果未变更且有缓存，则返回缓存
            if (_cachedClient != null &&
                _cachedModelId == modelId &&
                _cachedEndpoint == endpoint &&
                _cachedApiKey == apiKey)
            {
                return _cachedClient;
            }

            // 如果配置变更，重置错误状态
            _lastError = null;

            // 如果之前创建失败且配置未变，返回缓存的错误
            if (!string.IsNullOrEmpty(_lastError) &&
                _cachedModelId == modelId &&
                _cachedEndpoint == endpoint &&
                _cachedApiKey == apiKey)
                throw new FriendlyException(_lastError);

            try
            {
                if (string.IsNullOrWhiteSpace(modelId))
                    throw new FriendlyException("AI 功能未配置:请先在设置页面选择 AI 模型");
                if (string.IsNullOrWhiteSpace(apiKey))
                    throw new FriendlyException("AI 功能未配置:请先在设置页面配置 API Key");
                if (string.IsNullOrWhiteSpace(endpoint))
                    throw new FriendlyException("AI 功能未配置:请先在设置页面配置 API 端点");

                var openAIClient = new OpenAIClient(
                    new ApiKeyCredential(apiKey),
                    new OpenAIClientOptions
                    {
                        Endpoint = new Uri(endpoint)
                    }
                );

                _cachedClient = openAIClient.GetChatClient(modelId).AsIChatClient();

                // 更新缓存的配置
                _cachedModelId = modelId;
                _cachedEndpoint = endpoint;
                _cachedApiKey = apiKey;

                return _cachedClient;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                // 即使失败也记录当前配置，避免重复尝试相同配置
                _cachedModelId = modelId;
                _cachedEndpoint = endpoint;
                _cachedApiKey = apiKey;
                throw new FriendlyException(_lastError);
            }
        }
    }
}
