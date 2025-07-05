using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure;
using MarketAssistant.Plugins;
using Moq;

namespace TestMarketAssistant
{
    [TestClass]
    public sealed class StockTechnicalPluginTest : BaseKernelTest
    {
        private StockTechnicalPlugin _stockTechnicalPlugin = null!;

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
            _stockTechnicalPlugin = new StockTechnicalPlugin(httpClientFactory, mockUserSettingService.Object);
        }

        [TestMethod]
        public async Task TestGetStockKDJAsync()
        {
            var result = await _stockTechnicalPlugin.GetStockKDJAsync("sz002594");
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public async Task TestGetStockMACDAsync()
        {
            var result = await _stockTechnicalPlugin.GetStockMACDAsync("sz002594");
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public async Task TestGetStockBOLLAsync()
        {
            var result = await _stockTechnicalPlugin.GetStockBOLLAsync("sz002594");
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public async Task TestGetStockMAAsync()
        {
            var result = await _stockTechnicalPlugin.GetStockMAAsync("sz002594");
            Assert.IsNotNull(result);
        }
    }
}