using Microsoft.Extensions.DependencyInjection;

namespace TestMarketAssistant;

internal static class TestHttpClientServiceCollectionExtensions
{
    public static IServiceCollection AddTestMarketDataHttpClients(this IServiceCollection services)
    {
        services.AddHttpClient("Binance", client =>
        {
            client.BaseAddress = new Uri("https://api.binance.com");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddHttpClient("CoinGecko", client =>
        {
            client.BaseAddress = new Uri("https://api.coingecko.com/api/v3");
            client.Timeout = TimeSpan.FromSeconds(25);
        });

        services.AddHttpClient("CoinDesk", client =>
        {
            client.BaseAddress = new Uri("https://data-api.coindesk.com");
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        });

        services.AddHttpClient("ZhiTu", client =>
        {
            client.BaseAddress = new Uri("https://api.zhituapi.com");
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        return services;
    }
}
