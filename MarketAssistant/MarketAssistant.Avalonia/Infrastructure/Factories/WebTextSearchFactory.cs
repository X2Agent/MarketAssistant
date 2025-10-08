using MarketAssistant.Services.Settings;
using Microsoft.SemanticKernel.Plugins.Web.Bing;
using Microsoft.SemanticKernel.Plugins.Web.Brave;
using Microsoft.SemanticKernel.Plugins.Web.Tavily;
using Microsoft.SemanticKernel.Data;

namespace MarketAssistant.Infrastructure.Factories;

public interface IWebTextSearchFactory
{
    ITextSearch? Create();
}

public class WebTextSearchFactory(IUserSettingService userSettingService) : IWebTextSearchFactory
{
    public ITextSearch? Create()
    {
        var setting = userSettingService.CurrentSetting;
        if (!setting.EnableWebSearch || string.IsNullOrWhiteSpace(setting.WebSearchApiKey))
        {
            return null;
        }

        return setting.WebSearchProvider?.ToLowerInvariant() switch
        {
            "bing" => new BingTextSearch(apiKey: setting.WebSearchApiKey),
            "brave" => new BraveTextSearch(apiKey: setting.WebSearchApiKey),
            "tavily" => new TavilyTextSearch(apiKey: setting.WebSearchApiKey),
            _ => null
        };
    }
}

