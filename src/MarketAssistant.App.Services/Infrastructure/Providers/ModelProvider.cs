using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Infrastructure.Providers;

/// <summary>
/// 模型服务商定义。每个服务商通过代码注册于 <see cref="ModelProviderCatalog"/>。
/// </summary>
/// <param name="Id">唯一标识（与 UserSetting.ProviderId 对应）</param>
/// <param name="DisplayName">UI 显示名称</param>
/// <param name="DefaultEndpoint">最终 API Base URL，不再由适配器隐式追加版本后缀</param>
/// <param name="ApiKeyUrl">API Key 获取链接</param>
/// <param name="RequiresApiKey">服务商是否支持 API Key 配置；模型级是否强制由策略判断</param>
/// <param name="AllowsEndpointOverride">是否允许用户覆盖 API Base URL，仅本地或自定义部署开启</param>
/// <param name="SupportsModelListing">是否支持通过标准端点获取模型列表</param>
/// <param name="Protocol">服务商默认模型协议</param>
/// <param name="DefaultContextWindowTokens">服务商所有模型均可保证的上下文窗口；无法保证时必须为 null</param>
/// <param name="ModelContextWindowTokens">模型级上下文窗口，优先于服务商默认值</param>
/// <param name="ModelListingRequiresApiKey">模型列表端点是否需要 API Key，可与模型调用鉴权规则不同</param>
/// <param name="ModelListingUrlPath">模型列表相对路径，拼在 API Base URL 之后；默认 /models</param>
/// <param name="StructuredOutputMode">结构化任务使用的服务商级响应格式能力</param>
/// <param name="Policy">可选服务商特殊策略；单协议服务商使用默认策略</param>
public record ModelProvider(
    string Id,
    string DisplayName,
    string DefaultEndpoint,
    string? ApiKeyUrl,
    bool RequiresApiKey = true,
    bool AllowsEndpointOverride = false,
    bool SupportsModelListing = true,
    ModelApiProtocol Protocol = ModelApiProtocol.OpenAIChatCompletions,
    int? DefaultContextWindowTokens = null,
    IReadOnlyDictionary<string, int>? ModelContextWindowTokens = null,
    bool ModelListingRequiresApiKey = true,
    string ModelListingUrlPath = "/models",
    StructuredOutputMode StructuredOutputMode = StructuredOutputMode.JsonObject,
    IModelProviderPolicy? Policy = null)
{
    private IModelProviderPolicy EffectivePolicy => Policy ?? DefaultModelProviderPolicy.Instance;

    /// <summary>
    /// 获取指定模型使用的 API 协议。
    /// </summary>
    public ModelApiProtocol GetProtocol(string modelId) => EffectivePolicy.GetProtocol(this, modelId);

    /// <summary>
    /// 判断当前凭据是否允许访问模型列表。
    /// </summary>
    public bool CanListModels(string? apiKey) =>
        SupportsModelListing &&
        (!ModelListingRequiresApiKey || !string.IsNullOrWhiteSpace(apiKey));

    /// <summary>
    /// 判断指定模型是否需要 API Key。
    /// </summary>
    /// <remarks>
    /// “免费模型”不等于“匿名接口”。只有服务商通过模型目录或稳定命名约定明确确认无需鉴权时，才允许留空 API Key。
    /// </remarks>
    public bool RequiresApiKeyForModel(string? modelId) =>
        EffectivePolicy.RequiresApiKeyForModel(this, modelId);

    /// <summary>
    /// 获取模型上下文窗口。模型级显式配置优先，未知模型仅在服务商可保证统一下限时使用默认值。
    /// </summary>
    public int? GetContextWindowTokens(string modelId)
    {
        if (ModelContextWindowTokens?.TryGetValue(modelId, out var modelContextWindow) == true)
            return modelContextWindow > 0 ? modelContextWindow : null;

        return DefaultContextWindowTokens is > 0 ? DefaultContextWindowTokens : null;
    }
}

/// <summary>
/// 模型 API 协议。客户端创建直接使用对应官方 SDK。
/// </summary>
public enum ModelApiProtocol
{
    OpenAIChatCompletions,
    Ollama,
    Unsupported,
}
