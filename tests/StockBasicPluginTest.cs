using MarketAssistant.Agents.Plugins;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace TestMarketAssistant
{
    [TestClass]
    public sealed class StockBasicPluginTest
    {
        private StockBasicPlugin _stockBasicPlugin = null!;

        [TestInitialize]
        public void Initialize()
        {
            var zhiTuApiToken = Environment.GetEnvironmentVariable("ZHITU_API_TOKEN") ?? throw new InvalidOperationException("ZHITU_API_TOKEN environment variable is not set");

            var mockUserSettingService = new Mock<IUserSettingService>();
            mockUserSettingService.Setup(x => x.CurrentSetting).Returns(new UserSetting
            {
                ZhiTuApiToken = zhiTuApiToken
            });

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddHttpClient();
            serviceCollection.AddSingleton(mockUserSettingService.Object);
            var serviceProvider = serviceCollection.BuildServiceProvider();

            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            _stockBasicPlugin = new StockBasicPlugin(httpClientFactory, mockUserSettingService.Object);
        }

        [TestMethod]
        public async Task TestGetStockInfoAsync()
        {
            var result = await _stockBasicPlugin.GetStockInfoAsync("sz002594");
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public async Task TestGetStockCompanyInfoAsync()
        {
            var result = await _stockBasicPlugin.GetStockCompanyInfoAsync("sz002594");
            Assert.IsNotNull(result);
        }
    }
}