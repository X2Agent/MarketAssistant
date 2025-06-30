using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure;
using MarketAssistant.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Moq;

namespace TestMarketAssistant
{
    [TestClass]
    public sealed class StackInfoTest : BaseKernelTest
    {
        private StockDataPlugin _stockPlugin = null!;

        [TestInitialize]
        public void Initialize()
        {
            var kernel = CreateKernelWithChatCompletion();
            var userSettingServiceMock = new Mock<IUserSettingService>();
            userSettingServiceMock.Setup(x => x.CurrentSetting).Returns(new UserSetting { ZhiTuApiToken = "test-token" });
            var serviceProvider = new ServiceCollection()
                .AddSingleton(kernel)
                .AddLogging() // 添加日志服务
                .AddSingleton(userSettingServiceMock.Object)
                .BuildServiceProvider();
            _stockPlugin = new StockDataPlugin(serviceProvider, userSettingServiceMock.Object);
        }

        [TestMethod]
        public async Task TestGetStockPriceAsync()
        {
            // Arrange
            var stockSymbol = "sz002594";
            // Act
            var stockPriceInfo = await _stockPlugin.GetStockPriceAsync(stockSymbol);
            // Assert
            Assert.IsNotNull(stockPriceInfo);
            Assert.IsTrue(stockPriceInfo.CurrentPrice > 0);
            Assert.IsTrue(stockPriceInfo.PriceChange > 0);
            Assert.IsTrue(stockPriceInfo.PercentageChange > 0);
            Assert.IsTrue(stockPriceInfo.HighPrice > 0);
            Assert.IsTrue(stockPriceInfo.LowPrice > 0);
            Assert.IsTrue(stockPriceInfo.Volume > 0);
            Assert.IsTrue(stockPriceInfo.Amount > 0);
        }

        [TestMethod]
        public async Task TestGetFundFlowAsync()
        {
            var result = await _stockPlugin.GetFundFlowAsync("sz002594");
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public async Task TestGetStockCompanyInfoAsync()
        {
            var result = await _stockPlugin.GetStockCompanyInfoAsync("sz002594");
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public async Task TestGetNewsListAsync()
        {
            var result = await _stockPlugin.GetNewsListAsync("sz002594");
            Assert.IsTrue(result.Count() > 0);
        }

        [TestMethod]
        [DataRow("https://www.yicai.com/news/102505564.html")]
        [DataRow("https://www.time-weekly.com/post/319319")]
        [DataRow("https://www.cls.cn/detail/xk/67cfee033326ba97cb9269dc")]
        [DataRow("https://www.cls.cn")]
        public async Task TestGetNewsContentAsync(string url)
        {
            var result = await _stockPlugin.GetNewsContentAsync(url);
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public async Task TestGetQuarterlyProfitAsync()
        {
            // Arrange
            var stockSymbol = "sz002594";
            // Act
            var result = await _stockPlugin.GetQuarterlyProfitAsync(stockSymbol);
            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Count > 0);
            Assert.IsFalse(string.IsNullOrEmpty(result[0].Date));
            Assert.IsFalse(string.IsNullOrEmpty(result[0].Income));
            Assert.IsFalse(string.IsNullOrEmpty(result[0].NetProfit));
        }

        [TestMethod]
        public async Task TestGetQuarterlyCashFlowAsync()
        {
            // Arrange
            var stockSymbol = "sz002594";
            // Act
            var result = await _stockPlugin.GetQuarterlyCashFlowAsync(stockSymbol);
            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Count > 0);
            Assert.IsFalse(string.IsNullOrEmpty(result[0].Date));
            Assert.IsFalse(string.IsNullOrEmpty(result[0].OperatingCashflowNet));
            Assert.IsFalse(string.IsNullOrEmpty(result[0].InvestingCashflowNet));
        }

        [TestMethod]
        public async Task TestGetFinancialDataAsync()
        {
            // Arrange
            var stockSymbol = "sz002594";
            // Act
            var result = await _stockPlugin.GetFinancialDataAsync(stockSymbol);
            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Count > 0);
            Assert.IsFalse(string.IsNullOrEmpty(result[0].Date));
            Assert.IsFalse(string.IsNullOrEmpty(result[0].Jzsy));
            Assert.IsFalse(string.IsNullOrEmpty(result[0].Xsjl));
        }
    }
}
