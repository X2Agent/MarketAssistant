using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure;
using MarketAssistant.Plugins;
using Microsoft.SemanticKernel;
using Moq;

namespace TestMarketAssistant
{
    [TestClass]
    public sealed class StockNewsPluginTest : BaseKernelTest
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
        public async Task TestGetNewsListAsync()
        {
            var result = await _stockNewsPlugin.GetNewsListAsync("sz002594");
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public async Task TestGetNewsContentAsync()
        {
            var testUrl = "https://www.cls.cn/detail/1234567";
            var result = await _stockNewsPlugin.GetNewsContentAsync(testUrl);
            Assert.IsNotNull(result);
        }
    }
}