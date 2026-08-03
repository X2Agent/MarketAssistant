using MarketAssistant.Infrastructure.Providers;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;

namespace MarketAssistant.Infrastructure.Factories;

public class EmbeddingFactory : IEmbeddingFactory
{
    private readonly IUserSettingService _userSettingService;
    private readonly IModelProviderAdapterFactory _adapterFactory;

    public EmbeddingFactory(
        IUserSettingService userSettingService,
        IModelProviderAdapterFactory adapterFactory)
    {
        _userSettingService = userSettingService;
        _adapterFactory = adapterFactory;
    }

    public IEmbeddingGenerator<string, Embedding<float>> Create()
    {
        var userSetting = _userSettingService.CurrentSetting;
        var modelId = userSetting.EmbeddingModelId;
        var apiKey = userSetting.EmbeddingApiKey;
        var endpoint = userSetting.EmbeddingEndpoint;

        if (string.IsNullOrWhiteSpace(modelId))
            throw new FriendlyException("嵌入模型ID不能为空");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new FriendlyException("嵌入API密钥不能为空");

        // Embedding 使用独立的端点和密钥配置，默认通过 OpenAI 兼容协议接入
        var provider = new ModelProvider(
            Id: "Embedding",
            DisplayName: "Embedding Service",
            DefaultEndpoint: endpoint,
            ApiKeyUrl: null,
            RequiresApiKey: true
        );

        var adapter = _adapterFactory.Create(provider);
        return adapter.CreateEmbeddingGenerator(apiKey, modelId, endpoint)!;
    }
}
