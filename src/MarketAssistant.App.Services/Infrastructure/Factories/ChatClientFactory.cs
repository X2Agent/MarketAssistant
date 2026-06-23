using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using OpenAI;
using Polly;
using Polly.Retry;
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
/// 创建和缓存底层的 OpenAI ChatClient，并附加 LLM 瞬态错误重试管道
/// </summary>
public class ChatClientFactory : IChatClientFactory
{
    /// <summary>
    /// 瞬态错误冷却时间：冷却期内同一配置不重试，冷却后允许再次尝试
    /// </summary>
    private static readonly TimeSpan ErrorCooldown = TimeSpan.FromSeconds(30);

    /// <summary>
    /// LLM 调用重试管道：针对瞬态网络/服务端错误自动重试 2 次，指数退避 + 抖动
    /// 覆盖 Coordinator 和所有业务分析师的 LLM 调用
    /// </summary>
    private static readonly ResiliencePipeline LlmRetryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 2,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromSeconds(2),
            ShouldHandle = new PredicateBuilder()
                .Handle<HttpRequestException>()
                .Handle<TaskCanceledException>(ex => IsNetworkTimeout(ex))
        })
        .Build();

    /// <summary>
    /// 判断 TaskCanceledException 是否由网络超时引起（而非用户主动取消）。
    /// System.ClientModel 超时时抛出的异常链为：
    /// TaskCanceledException → TaskCanceledException → IOException → SocketException
    /// </summary>
    private static bool IsNetworkTimeout(TaskCanceledException ex)
    {
        if (ex.InnerException is TimeoutException) return true;
        if (ex.CancellationToken.IsCancellationRequested) return false;
        // System.ClientModel 的超时消息包含 "exceeded the configured timeout"
        return ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
    }

    private readonly IUserSettingService _userSettingService;
    private readonly object _lock = new();
    private IChatClient? _cachedClient;
    private string? _lastError;
    private DateTime _lastErrorTime;

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
        IChatClient? oldClient = null;
        try
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

                // 配置未变且上次失败仍在冷却期内 → 快速失败，避免频繁重试
                if (configUnchanged
                    && !string.IsNullOrEmpty(_lastError)
                    && DateTime.UtcNow - _lastErrorTime < ErrorCooldown)
                {
                    throw new FriendlyException(_lastError);
                }

                // 配置已变更或冷却期已过，重置错误状态
                _lastError = null;
                // 保存旧客户端引用，稍后在 lock 外 Dispose（避免持锁等待网络连接关闭）
                oldClient = _cachedClient;
                _cachedClient = null;

                try
                {
                    if (string.IsNullOrWhiteSpace(modelId))
                        throw new FriendlyException("AI 功能未配置:请先在设置页面选择 AI 模型");
                    if (string.IsNullOrWhiteSpace(apiKey))
                        throw new FriendlyException("AI 功能未配置:请先在设置页面配置 API Key");
                    if (string.IsNullOrWhiteSpace(endpoint))
                        throw new FriendlyException("AI 功能未配置:请先在设置页面配置 API 端点");

                    // 与 EmbeddingFactory 保持一致：OpenAI SDK 需要带 /v1 的 base URL
                    // 规范化处理：去掉末尾斜杠，若未包含 /v1 则追加，避免重复拼接
                    var normalizedEndpoint = endpoint.TrimEnd('/');
                    if (!normalizedEndpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                    {
                        normalizedEndpoint += "/v1";
                    }

                    var openAIClient = new OpenAIClient(
                        new ApiKeyCredential(apiKey),
                        new OpenAIClientOptions
                        {
                            Endpoint = new Uri(normalizedEndpoint),
                            // 分析工作流中 Agent 使用流式调用，Tool-Call 链路在等待外部 API
                            // 返回期间不产生 token，默认 100s 超时过于激进。
                            // 设为 3 分钟兼顾长链路 Tool-Call 和异常检测。
                            NetworkTimeout = TimeSpan.FromMinutes(3)
                        }
                    );

                    // 使用 ResilientChatClient 装饰器附加重试管道，所有 LLM 调用自动获得瞬态错误重试
                    var rawClient = openAIClient.GetChatClient(modelId).AsIChatClient();
                    _cachedClient = new ResilientChatClient(rawClient, LlmRetryPipeline);

                    _cachedModelId = modelId;
                    _cachedEndpoint = endpoint;
                    _cachedApiKey = apiKey;

                    return _cachedClient;
                }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                    _lastErrorTime = DateTime.UtcNow;
                    _cachedClient = null;
                    _cachedModelId = modelId;
                    _cachedEndpoint = endpoint;
                    _cachedApiKey = apiKey;
                    throw new FriendlyException(_lastError);
                }
            }
        }
        finally
        {
            // 在 lock 外 Dispose 旧客户端，避免持锁等待网络连接关闭
            oldClient?.Dispose();
        }
    }
}
