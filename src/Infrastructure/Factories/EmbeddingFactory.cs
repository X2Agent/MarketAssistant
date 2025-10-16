using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

namespace MarketAssistant.Infrastructure.Factories;

public interface IEmbeddingFactory
{
    IEmbeddingGenerator<string, Embedding<float>> Create();
}

public class EmbeddingFactory : IEmbeddingFactory
{
    private readonly IUserSettingService _userSettingService;
    public EmbeddingFactory(IUserSettingService userSettingService)
    {
        _userSettingService = userSettingService;
    }

    public IEmbeddingGenerator<string, Embedding<float>> Create()
    {
        var userSetting = _userSettingService.CurrentSetting;
        var modelId = userSetting.EmbeddingModelId;
        var apiKey = userSetting.ApiKey;
        var endpoint = userSetting.Endpoint;

        if (string.IsNullOrWhiteSpace(modelId))
            throw new FriendlyException("嵌入模型ID不能为空");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new FriendlyException("API密钥不能为空");

        var client = string.IsNullOrWhiteSpace(endpoint)
            ? new OpenAIClient(apiKey)
            : new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions
            {
                Endpoint = new Uri(endpoint + "/v1")
            });

        return client.GetEmbeddingClient(modelId).AsIEmbeddingGenerator();
    }
}

