namespace MarketAssistant.Infrastructure.Providers;

/// <summary>
/// 模型服务商定义。每个服务商通过代码注册于 <see cref="ModelProviderCatalog"/>。
/// </summary>
/// <param name="Id">唯一标识（与 UserSetting.ProviderId 对应）</param>
/// <param name="DisplayName">UI 显示名称</param>
/// <param name="DefaultEndpoint">最终 API Base URL，不再由适配器隐式追加版本后缀</param>
/// <param name="ApiKeyUrl">API Key 获取链接</param>
/// <param name="RequiresApiKey">是否需要 API Key（本地部署如 Ollama 设为 false）</param>
/// <param name="SupportsModelListing">是否支持通过标准端点获取模型列表</param>
/// <param name="AdapterKind">适配器类型，决定创建 IChatClient 的方式</param>
/// <param name="SupportedModelIdPrefixes">模型发现结果的可选白名单前缀，用于过滤同一网关中的非兼容协议模型</param>
/// <param name="DefaultContextWindowTokens">服务商所有模型均可保证的上下文窗口；无法保证时必须为 null</param>
/// <param name="ModelContextWindowTokens">模型级上下文窗口，优先于服务商默认值</param>
/// <param name="ApiKeyOptionalModelIds">已明确确认服务端匿名可用的模型 ID；模型“免费”但仍需鉴权时不要加入</param>
public record ModelProvider(
    string Id,
    string DisplayName,
    string DefaultEndpoint,
    string? ApiKeyUrl,
    bool RequiresApiKey = true,
    bool SupportsModelListing = true,
    ProviderAdapterKind AdapterKind = ProviderAdapterKind.OpenAICompatible,
    IReadOnlyList<string>? SupportedModelIdPrefixes = null,
    int? DefaultContextWindowTokens = null,
    IReadOnlyDictionary<string, int>? ModelContextWindowTokens = null,
    IReadOnlyList<string>? ApiKeyOptionalModelIds = null)
{
    /// <summary>
    /// 判断模型是否兼容当前服务商适配器。
    /// </summary>
    public bool IsModelSupported(string modelId)
    {
        if (SupportedModelIdPrefixes is not { Count: > 0 })
            return true;

        return SupportedModelIdPrefixes.Any(prefix =>
            modelId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 判断指定模型是否需要 API Key。
    /// </summary>
    /// <remarks>
    /// “免费模型”不等于“匿名接口”。只有服务商或模型目录明确确认无需鉴权时，才允许留空 API Key。
    /// </remarks>
    public bool RequiresApiKeyForModel(string? modelId)
    {
        if (!RequiresApiKey)
            return false;

        if (string.IsNullOrWhiteSpace(modelId))
            return true;

        return ApiKeyOptionalModelIds?.Contains(modelId, StringComparer.OrdinalIgnoreCase) != true;
    }

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
/// 适配器种类，决定 <see cref="IModelProviderAdapter"/> 的具体实现
/// </summary>
public enum ProviderAdapterKind
{
    /// <summary>
    /// OpenAI 兼容协议（覆盖绝大多数国内服务商 + OpenAI）
    /// </summary>
    OpenAICompatible,

    /// <summary>
    /// Ollama 原生协议（使用 OllamaSharp SDK）
    /// </summary>
    Ollama,
}
