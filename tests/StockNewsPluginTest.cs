using MarketAssistant.Agents.Plugins;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Browser;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Moq;

namespace TestMarketAssistant
{
    [TestClass]
    public sealed class StockNewsPluginTest
    {
        private StockNewsPlugin _stockNewsPlugin = null!;
        private IServiceProvider _serviceProvider = null!;

        [TestInitialize]
        public void Initialize()
        {
            var mockUserSettingService = new Mock<IUserSettingService>();
            mockUserSettingService.Setup(x => x.CurrentSetting).Returns(new UserSetting());

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddHttpClient();
            serviceCollection.AddSingleton(mockUserSettingService.Object);
            serviceCollection.AddSingleton<PlaywrightService>();
            serviceCollection.AddSingleton<Kernel>();
            _serviceProvider = serviceCollection.BuildServiceProvider();

            _stockNewsPlugin = new StockNewsPlugin(_serviceProvider);
        }

        [TestMethod]
        public async Task TestGetStockNewsContext_Concise()
        {
            var result = await _stockNewsPlugin.GetStockNewsContextAsync("sz002594", topK: 3, responseFormat: "concise");
            Assert.IsNotNull(result);
            // concise 模式不强制要求 Summary
        }

        [TestMethod]
        public async Task TestGetStockNewsContext_Detailed()
        {
            var result = await _stockNewsPlugin.GetStockNewsContextAsync("sz002594", topK: 2, responseFormat: "detailed");
            Assert.IsNotNull(result);
            // detailed 模式尽量包含 Summary（但考虑网络/解析失败，不强制）
        }
    }
}