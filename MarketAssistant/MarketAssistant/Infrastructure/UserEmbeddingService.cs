using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

namespace MarketAssistant.Infrastructure;

public interface IUserEmbeddingService
{
    IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator();
}

public class UserEmbeddingService(IUserSettingService userSettingService) : IUserEmbeddingService
{
    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator()
    {
        var userSetting = userSettingService.CurrentSetting;
        var modelId = userSetting.EmbeddingModelId;
        var apiKey = userSetting.ApiKey;
        var endpoint = userSetting.Endpoint;

        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("嵌入模型ID不能为空", nameof(modelId));
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API密钥不能为空", nameof(apiKey));

        var client = string.IsNullOrWhiteSpace(endpoint)
            ? new OpenAIClient(apiKey)
            : new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions
            {
                Endpoint = new Uri(endpoint + "/v1")
            });

        return client.GetEmbeddingClient(modelId).AsIEmbeddingGenerator();
    }
}