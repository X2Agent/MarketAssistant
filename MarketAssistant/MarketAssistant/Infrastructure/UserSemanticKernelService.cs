using MarketAssistant.Applications.Settings;
using MarketAssistant.Plugins;
using MarketAssistant.Vectors;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Data;
using Microsoft.SemanticKernel.Plugins.Core;
using Microsoft.SemanticKernel.Plugins.Web.Bing;
using Microsoft.SemanticKernel.Plugins.Web.Brave;
using Microsoft.SemanticKernel.Plugins.Web.Tavily;

namespace MarketAssistant.Infrastructure;

public interface IUserSemanticKernelService
{
    Kernel GetKernel();
}

internal class UserSemanticKernelService(
        IUserSettingService userSettingService,
        PlaywrightService playwrightService,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        VectorStore vectorStore) : IUserSemanticKernelService
{

    public Kernel GetKernel()
    {
        var userSetting = userSettingService.CurrentSetting;
        if (string.IsNullOrWhiteSpace(userSetting.ModelId))
            throw new ArgumentException("ModelId 不能为空。", nameof(userSetting.ModelId));
        if (string.IsNullOrWhiteSpace(userSetting.ApiKey))
            throw new ArgumentException("ApiKey 不能为空。", nameof(userSetting.ApiKey));
        if (string.IsNullOrWhiteSpace(userSetting.Endpoint))
            throw new ArgumentException("Endpoint 不能为空。", nameof(userSetting.Endpoint));

        var builder = Kernel.CreateBuilder();

        // 将主容器的关键服务注册到 Kernel 的独立容器中
        builder.Services.AddSingleton(userSettingService);
        builder.Services.AddSingleton(playwrightService);

        // 使用用户提供的配置添加聊天补全服务
        builder.AddOpenAIChatCompletion(
           userSetting.ModelId,
           new Uri(userSetting.Endpoint),
           userSetting.ApiKey);

        // 如果启用了Web Search功能且提供了有效的API Key，则添加Web Search服务
        if (userSetting.EnableWebSearch && !string.IsNullOrWhiteSpace(userSetting.WebSearchApiKey))
        {
            ITextSearch textSearch = null;
            // 根据用户选择的搜索服务商添加相应的搜索服务
            switch (userSetting.WebSearchProvider)
            {
                case "Bing":
                    textSearch = new BingTextSearch(apiKey: userSetting.WebSearchApiKey);
                    break;
                case "Brave":
                    textSearch = new BraveTextSearch(apiKey: userSetting.WebSearchApiKey);
                    break;
                case "Tavily":
                    textSearch = new TavilyTextSearch(apiKey: userSetting.WebSearchApiKey);
                    break;
            }

            var searchPlugin = textSearch.CreateWithSearch("SearchPlugin");
            builder.Plugins.Add(searchPlugin);
        }

        if (userSetting.LoadKnowledge)
        {
            var collection = vectorStore.GetCollection<string, TextParagraph>(UserSetting.VectorCollectionName);
            var textSearch = new VectorStoreTextSearch<TextParagraph>(collection, embeddingGenerator);

            // Build a text search plugin with vector store search and add to the kernel
            var searchPlugin = textSearch.CreateWithGetTextSearchResults("VectorSearchPlugin");
            builder.Plugins.Add(searchPlugin);
        }

        builder.Plugins.AddFromType<StockDataPlugin>()
            .AddFromType<StockKLinePlugin>()
            .AddFromType<ConversationSummaryPlugin>()
            .AddFromType<TextPlugin>();

        builder.Plugins.AddFromPromptDirectory(Path.Combine(Directory.GetCurrentDirectory(), "Plugins", "Yaml"));
        //builder.Plugins.AddFromFunctions("mcp", await McpPlugin.GetKernelFunctionsAsync());

        return builder.Build();
    }
}
