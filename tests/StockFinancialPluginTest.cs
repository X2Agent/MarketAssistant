using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure;
using MarketAssistant.Plugins;
using Moq;

namespace TestMarketAssistant
{
    [TestClass]
    public sealed class StockFinancialPluginTest
    {
        private StockFinancialPlugin _stockFinancialPlugin = null!;

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
            _stockFinancialPlugin = new StockFinancialPlugin(httpClientFactory, mockUserSettingService.Object);
        }

        [TestMethod]
        public async Task TestGetFundFlowAsync()
        {
            var result = await _stockFinancialPlugin.GetFundFlowAsync("sz002594");
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public async Task TestGetFinancialDataAsync()
        {
            var result = await _stockFinancialPlugin.GetFinancialDataAsync("sz002594");
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public async Task TestGetQuarterlyProfitAsync()
        {
            var result = await _stockFinancialPlugin.GetQuarterlyProfitAsync("sz002594");
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public async Task TestGetQuarterlyCashFlowAsync()
        {
            var result = await _stockFinancialPlugin.GetQuarterlyCashFlowAsync("sz002594");
            Assert.IsNotNull(result);
        }
    }
}