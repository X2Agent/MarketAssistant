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

            bool configUnchanged = _cachedModelId == modelId
                                && _cachedEndpoint == endpoint
                                && _cachedApiKey == apiKey;

            // 配置未变且有成功缓存 → 直接返回
            if (configUnchanged && _cachedClient != null)
            {
                return _cachedClient;
            }

            // 配置未变但上次创建失败 → 快速失败，避免重复尝试
            if (configUnchanged && !string.IsNullOrEmpty(_lastError))
            {
                throw new FriendlyException(_lastError);
            }

            // 配置已变更，重置错误状态
            _lastError = null;
            _cachedClient = null;

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
                _cachedModelId = modelId;
                _cachedEndpoint = endpoint;
                _cachedApiKey = apiKey;

                return _cachedClient;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _cachedClient = null;
                _cachedModelId = modelId;
                _cachedEndpoint = endpoint;
                _cachedApiKey = apiKey;
                throw new FriendlyException(_lastError);
            }
        }
    }
}
