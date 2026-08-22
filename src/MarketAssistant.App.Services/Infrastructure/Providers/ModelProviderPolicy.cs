namespace MarketAssistant.Infrastructure.Providers;

/// <summary>
/// 定义服务商在模型协议和鉴权方面的特殊规则。
/// </summary>
public interface IModelProviderPolicy
{
    ModelApiProtocol GetProtocol(ModelProvider provider, string modelId);

    bool RequiresApiKeyForModel(ModelProvider provider, string? modelId);
}

/// <summary>
/// 单协议服务商的默认规则。
/// </summary>
internal sealed class DefaultModelProviderPolicy : IModelProviderPolicy
{
    public static DefaultModelProviderPolicy Instance { get; } = new();

    private DefaultModelProviderPolicy()
    {
    }

    public ModelApiProtocol GetProtocol(ModelProvider provider, string modelId) => provider.Protocol;

    public bool RequiresApiKeyForModel(ModelProvider provider, string? modelId) => provider.RequiresApiKey;
}

/// <summary>
/// OpenCode Zen 多协议网关规则。当前应用仅接入其 OpenAI Chat Completions 模型。
/// </summary>
internal sealed class OpenCodeZenModelPolicy : IModelProviderPolicy
{
    private static readonly string[] ChatCompletionModelPrefixes =
    [
        "deepseek-",
        "minimax-",
        "glm-",
        "kimi-",
        "mimo-",
        "laguna-",
        "ling-",
        "north-",
        "nemotron-"
    ];

    public static OpenCodeZenModelPolicy Instance { get; } = new();

    private OpenCodeZenModelPolicy()
    {
    }

    public ModelApiProtocol GetProtocol(ModelProvider provider, string modelId)
    {
        if (modelId.Equals("big-pickle", StringComparison.OrdinalIgnoreCase) ||
            ChatCompletionModelPrefixes.Any(prefix =>
                modelId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return ModelApiProtocol.OpenAIChatCompletions;
        }

        // Zen 是多协议网关。未知模型默认拒绝，避免误发到 /chat/completions。
        return ModelApiProtocol.Unsupported;
    }

    public bool RequiresApiKeyForModel(ModelProvider provider, string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return true;

        return !modelId.Equals("big-pickle", StringComparison.OrdinalIgnoreCase) &&
               !modelId.EndsWith("-free", StringComparison.OrdinalIgnoreCase);
    }

}
