using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Infrastructure.Providers;

/// <summary>
/// 服务商注册表。所有地址均为可直接交给对应 SDK 的最终 API Base URL。
/// </summary>
public static class ModelProviderCatalog
{
    /// <summary>
    /// ��有预置服务商（顺序即 UI 显示顺序）。
    /// </summary>
    public static readonly IReadOnlyList<ModelProvider> Providers =
    [
        // 国内服务商
        new(
            Id: "SiliconFlow",
            DisplayName: "硅基流动",
            DefaultEndpoint: "https://api.siliconflow.cn/v1",
            ApiKeyUrl: "https://cloud.siliconflow.cn/i/z4lbHdBE"),
        new(
            Id: "DeepSeek",
            DisplayName: "DeepSeek",
            DefaultEndpoint: "https://api.deepseek.com",
            ApiKeyUrl: "https://platform.deepseek.com/api_keys",
            ModelContextWindowTokens: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["deepseek-v4-flash"] = 1_000_000,
                ["deepseek-v4-pro"] = 1_000_000
            }),
        new(
            Id: "Moonshot",
            DisplayName: "月之暗面 (Kimi)",
            DefaultEndpoint: "https://api.moonshot.cn/v1",
            ApiKeyUrl: "https://platform.moonshot.cn/console/api-keys"),
        new(
            Id: "Zhipu",
            DisplayName: "智谱 AI",
            DefaultEndpoint: "https://open.bigmodel.cn/api/paas/v4",
            ApiKeyUrl: "https://open.bigmodel.cn/usercenter/apikeys"),
        new(
            Id: "Qwen",
            DisplayName: "通义千问",
            DefaultEndpoint: "https://dashscope.aliyuncs.com/compatible-mode/v1",
            ApiKeyUrl: "https://dashscope.console.aliyun.com/apiKey"),
        new(
            Id: "Baichuan",
            DisplayName: "百川",
            DefaultEndpoint: "https://api.baichuan-ai.com/v1",
            ApiKeyUrl: "https://platform.baichuan-ai.com/console/apikey"),
        new(
            Id: "MiniMax",
            DisplayName: "MiniMax",
            DefaultEndpoint: "https://api.minimax.chat/v1",
            ApiKeyUrl: "https://platform.minimaxi.com/user-center/basic-information/interface-key"),
        new(
            Id: "Doubao",
            DisplayName: "字节豆包",
            DefaultEndpoint: "https://ark.cn-beijing.volces.com/api/v3",
            ApiKeyUrl: "https://console.volcengine.com/ark/region:ark+cn-beijing/apiKey"),
        new(
            Id: "ModelScope",
            DisplayName: "魔搭社区",
            DefaultEndpoint: "https://api-inference.modelscope.cn/v1",
            ApiKeyUrl: "https://modelscope.cn/my/myaccesstoken"),
        new(
            Id: "PPIO",
            DisplayName: "PPIO 派欧云",
            DefaultEndpoint: "https://api.ppio.com/openai/v1",
            ApiKeyUrl: "https://console.ppinfra.com/user/token"),
        new(
            Id: "HuaweiCloud",
            DisplayName: "华为云",
            DefaultEndpoint: "https://infer-models.cn-southwest-2.myhuaweicloud.com/v1",
            ApiKeyUrl: "https://console.huaweicloud.com/maas/management/key"),

        // 国外服务商
        new(
            Id: "OpenCodeZen",
            DisplayName: "OpenCode Zen",
            DefaultEndpoint: "https://opencode.ai/zen/v1",
            ApiKeyUrl: "https://opencode.ai/auth",
            ModelListingRequiresApiKey: false,
            Policy: OpenCodeZenModelPolicy.Instance),
        new(
            Id: "OpenAI",
            DisplayName: "OpenAI",
            DefaultEndpoint: "https://api.openai.com/v1",
            ApiKeyUrl: "https://platform.openai.com/api-keys",
            StructuredOutputMode: StructuredOutputMode.JsonSchema),
        new(
            Id: "OpenRouter",
            DisplayName: "OpenRouter",
            DefaultEndpoint: "https://openrouter.ai/api/v1",
            ApiKeyUrl: "https://openrouter.ai/keys"),
        new(
            Id: "Groq",
            DisplayName: "Groq",
            DefaultEndpoint: "https://api.groq.com/openai/v1",
            ApiKeyUrl: "https://console.groq.com/keys"),
        new(
            Id: "Grok",
            DisplayName: "Grok (xAI)",
            DefaultEndpoint: "https://api.x.ai/v1",
            ApiKeyUrl: "https://console.x.ai"),
        new(
            Id: "Mistral",
            DisplayName: "Mistral AI",
            DefaultEndpoint: "https://api.mistral.ai/v1",
            ApiKeyUrl: "https://console.mistral.ai/api-keys"),
        new(
            Id: "Together",
            DisplayName: "Together AI",
            DefaultEndpoint: "https://api.together.xyz/v1",
            ApiKeyUrl: "https://api.together.ai/settings/api-keys"),
        new(
            Id: "Perplexity",
            DisplayName: "Perplexity",
            DefaultEndpoint: "https://api.perplexity.ai",
            ApiKeyUrl: "https://www.perplexity.ai/settings/api"),

        // 本地部署
        new(
            Id: "Ollama",
            DisplayName: "Ollama (本地)",
            DefaultEndpoint: "http://localhost:11434",
            ApiKeyUrl: null,
            RequiresApiKey: false,
            AllowsEndpointOverride: true,
            ModelListingRequiresApiKey: false,
            Protocol: ModelApiProtocol.Ollama),
        new(
            Id: "LMStudio",
            DisplayName: "LM Studio (本地)",
            DefaultEndpoint: "http://localhost:1234/v1",
            ApiKeyUrl: null,
            RequiresApiKey: false,
            AllowsEndpointOverride: true,
            ModelListingRequiresApiKey: false),

        // 自定义 OpenAI 兼容服务
        new(
            Id: "Custom",
            DisplayName: "自定义",
            DefaultEndpoint: string.Empty,
            ApiKeyUrl: null,
            AllowsEndpointOverride: true,
            SupportsModelListing: false,
            StructuredOutputMode: StructuredOutputMode.Text),
    ];

    /// <summary>
    /// 根据 ID 获取服务商定义。
    /// </summary>
    public static ModelProvider? GetProvider(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return Providers.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }
}
