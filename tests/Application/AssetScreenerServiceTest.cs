using MarketAssistant.Applications.AssetScreener;
using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services;
using MarketAssistant.Services.Browser;
using MarketAssistant.Services.Data;
using MarketAssistant.Services.Settings;
using MarketAssistant.Services.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace TestMarketAssistant.Application;

/// <summary>
/// IAssetScreenerService 接口测试（覆盖 A股 和 虚拟币 实现）
/// </summary>
[TestClass]
public sealed class AssetScreenerServiceTest
{
    private ServiceProvider? _serviceProvider;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();

        // 注册日志服务
        services.AddLogging();

        // 注册 A股 筛选服务依赖
        var mockUserSettingService = new Mock<IUserSettingService>();
        mockUserSettingService.Setup(s => s.CurrentSetting)
            .Returns(new UserSetting
            {
                ModelId = "test-model",
                Endpoint = "http://localhost",
                ApiKey = "test-key"
            });
        services.AddSingleton(mockUserSettingService.Object);
        services.AddSingleton<PlaywrightService>();
        services.AddLogging();
        services.AddHttpClient();
        services.AddTestMarketDataHttpClients();
        services.AddMemoryCache();

        // 注册虚拟币筛选服务依赖
        services.AddSingleton<CoinGeckoApiService>();
        services.AddSingleton<BinanceMarketDataService>();

        // 注册被测试的服务
        services.AddKeyedSingleton<IAssetScreenerService, StockScreenerService>(MarketType.AShare);
        services.AddKeyedSingleton<IAssetScreenerService, CryptoScreenerService>(MarketType.Crypto);

        _serviceProvider = services.BuildServiceProvider();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_serviceProvider != null)
        {
            await _serviceProvider.DisposeAsync();
        }
    }

    #region A股筛选测试

    [TestMethod]
    [TestCategory("Integration")]
    public void Constructor_AShare_ShouldCreateInstance()
    {
        // Arrange & Act
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetScreenerService>(MarketType.AShare);

        // Assert
        Assert.IsNotNull(service);
    }


    [TestMethod]
    [TestCategory("Integration")]
    public async Task ScreenAsync_AShare_WithDefaultCriteria_ShouldReturnStocks()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetScreenerService>(MarketType.AShare);
        var criteria = new StockCriteria
        {
            Market = AShareType.AllAShares,
            Industry = IndustryType.All,
            Limit = 10,
            Criteria = new List<StockScreeningCriteria>()
        };

        // Act
        var result = await service.ScreenAsync(criteria);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count >= 0);
        Assert.IsTrue(result.Count <= criteria.Limit);

        Console.WriteLine($"A股默认条件筛选 - 返回股票数量: {result.Count}");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task ScreenAsync_AShare_WithSingleCriteria_ShouldReturnFilteredStocks()
    {
        // Arrange - 测试单个条件筛选（市值）
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetScreenerService>(MarketType.AShare);
        var criteria = new StockCriteria
        {
            Market = AShareType.AllAShares,
            Industry = IndustryType.All,
            Limit = 15,
            Criteria = new List<StockScreeningCriteria>
            {
                new()
                {
                    Code = "mc",
                    DisplayName = "总市值",
                    MinValue = 5000000000m,  // 50亿元
                    MaxValue = 25000000000m  // 250亿元
                }
            }
        };

        // Act
        var result = await service.ScreenAsync(criteria);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count >= 0);
        Assert.IsTrue(result.Count <= criteria.Limit);

        Console.WriteLine($"A股单条件筛选（市值50-250亿） - 返回股票数量: {result.Count}");
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task ScreenAsync_AShare_WithMultipleCriteria_ShouldReturnFilteredStocks()
    {
        // Arrange - 测试多条件组合筛选
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetScreenerService>(MarketType.AShare);
        var criteria = new StockCriteria
        {
            Market = AShareType.AllAShares,
            Industry = IndustryType.All,
            Limit = 20,
            Criteria = new List<StockScreeningCriteria>
            {
                new()
                {
                    Code = "mc",
                    DisplayName = "总市值",
                    MinValue = 50000000000m,   // 500亿元
                    MaxValue = 1000000000000m  // 1万亿元
                },
                new()
                {
                    Code = "pettm",
                    DisplayName = "市盈率TTM",
                    MinValue = 5m,
                    MaxValue = 50m
                }
            }
        };

        // Act
        var result = await service.ScreenAsync(criteria);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.Count >= 0);
        Assert.IsTrue(result.Count <= criteria.Limit);

        Console.WriteLine($"A股多条件筛选（市值+PE） - 返回股票数量: {result.Count}");
    }

    #endregion

    #region 虚拟币筛选测试

    [TestMethod]
    [TestCategory("Integration")]
    public void Constructor_Crypto_ShouldCreateInstance()
    {
        // Arrange & Act
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetScreenerService>(MarketType.Crypto);

        // Assert
        Assert.IsNotNull(service);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task ScreenAsync_Crypto_WithDefaultCriteria_ShouldReturnCryptos()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetScreenerService>(MarketType.Crypto);
        var criteria = new CryptoCriteria
        {
            Limit = 10,
            Criteria = new List<CryptoScreeningCondition>()
        };

        // Act
        try
        {
            var result = await service.ScreenAsync(criteria);
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Count >= 0);
            Assert.IsTrue(result.Count <= criteria.Limit);
            Assert.IsTrue(result.All(r => !string.IsNullOrEmpty(r.Symbol)));

            Console.WriteLine($"虚拟币默认条件筛选 - 返回数量: {result.Count}");
        }
        catch (FriendlyException ex)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(ex.Message));
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task ScreenAsync_Crypto_WithMarketCapFilter_ShouldReturnFilteredCryptos()
    {
        // Arrange - 测试市值筛选
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetScreenerService>(MarketType.Crypto);
        var criteria = new CryptoCriteria
        {
            Limit = 15,
            Criteria = new List<CryptoScreeningCondition>
            {
                new()
                {
                    Code = "market_cap",
                    MinValue = 1_000_000_000m,  // 10亿美元
                    MaxValue = 50_000_000_000m  // 500亿美元
                }
            }
        };

        // Act
        try
        {
            var result = await service.ScreenAsync(criteria);
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Count >= 0);
            Assert.IsTrue(result.Count <= criteria.Limit);

            Console.WriteLine($"虚拟币市值筛选（10-500亿美元） - 返回数量: {result.Count}");
        }
        catch (FriendlyException ex)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(ex.Message));
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task ScreenAsync_Crypto_WithPriceChangeFilter_ShouldReturnFilteredCryptos()
    {
        // Arrange - 测试价格变化筛选
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetScreenerService>(MarketType.Crypto);
        var criteria = new CryptoCriteria
        {
            Limit = 20,
            Criteria = new List<CryptoScreeningCondition>
            {
                new()
                {
                    Code = "price_change_24h",
                    MinValue = -10m,  // 跌幅不超过10%
                    MaxValue = 50m    // 涨幅50%以下
                }
            }
        };

        // Act
        try
        {
            var result = await service.ScreenAsync(criteria);
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Count >= 0);
            Assert.IsTrue(result.Count <= criteria.Limit);

            Console.WriteLine($"虚拟币价格变化筛选（-10% ~ +50%） - 返回数量: {result.Count}");
        }
        catch (FriendlyException ex)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(ex.Message));
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task ScreenAsync_Crypto_WithMultipleCriteria_ShouldReturnFilteredCryptos()
    {
        // Arrange - 测试多条件组合筛选
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetScreenerService>(MarketType.Crypto);
        var criteria = new CryptoCriteria
        {
            Limit = 15,
            Criteria = new List<CryptoScreeningCondition>
            {
                new()
                {
                    Code = "market_cap",
                    MinValue = 5_000_000_000m,   // 50亿美元
                    MaxValue = 100_000_000_000m  // 1000亿美元
                },
                new()
                {
                    Code = "volume_24h",
                    MinValue = 100_000_000m      // 24小时交易量1亿美元以上
                }
            }
        };

        // Act
        try
        {
            var result = await service.ScreenAsync(criteria);
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Count >= 0);
            Assert.IsTrue(result.Count <= criteria.Limit);

            Console.WriteLine($"虚拟币多条件筛选（市值+交易量） - 返回数量: {result.Count}");
        }
        catch (FriendlyException ex)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(ex.Message));
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task ScreenAsync_Crypto_WithMarketCapRankFilter_ShouldReturnTopCryptos()
    {
        // Arrange - 测试市值排名筛选
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetScreenerService>(MarketType.Crypto);
        var criteria = new CryptoCriteria
        {
            Limit = 10,
            Criteria = new List<CryptoScreeningCondition>
            {
                new()
                {
                    Code = "market_cap_rank",
                    MinValue = 1m,
                    MaxValue = 50m
                }
            }
        };

        // Act
        try
        {
            var result = await service.ScreenAsync(criteria);
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Count >= 0);
            Assert.IsTrue(result.Count <= criteria.Limit);

            Console.WriteLine($"虚拟币市值排名筛选（前50名） - 返回数量: {result.Count}");
        }
        catch (FriendlyException ex)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(ex.Message));
        }
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task ScreenAsync_Crypto_WithInvalidCriteriaType_ShouldThrowArgumentException()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetScreenerService>(MarketType.Crypto);
        var invalidCriteria = new StockCriteria(); // 错误的类型

        // Act & Assert
        await Assert.ThrowsExactlyAsync<ArgumentException>(
            async () => await service.ScreenAsync(invalidCriteria));
    }

    #endregion
}

