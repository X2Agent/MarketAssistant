using MarketAssistant.Applications.AssetScreener;
using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Applications.Settings;
using MarketAssistant.DataProviders;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
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
                ProviderId = "Custom",
                ProviderModelIds = new Dictionary<string, string> { ["Custom"] = "test-model" },
                ProviderEndpoints = new Dictionary<string, string> { ["Custom"] = "http://localhost" },
                ProviderApiKeys = new Dictionary<string, string> { ["Custom"] = "test-key" }
            });
        services.AddSingleton(mockUserSettingService.Object);
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
    public async Task ScreenAsync_AShare_WithDefaultCriteria_ShouldReturnStocks()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetScreenerService>(MarketType.AShare);
        var criteria = new StockCriteria
        {
            Market = AShareType.AllAShares,
            Industry = IndustryType.All,
            Limit = 10,
            Criteria = []
        };

        // Act
        var result = await service.ScreenAsync(criteria);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotEmpty(result, "A股默认条件筛选应返回至少一只股票");
        Assert.IsLessThanOrEqualTo(criteria.Limit, result.Count, "返回数量不应超过 Limit");
        Assert.IsTrue(result.All(r => !string.IsNullOrWhiteSpace(r.Symbol)), "所有结果应包含有效代码");

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
            Criteria =
            [
                new()
                {
                    Code = "mc",
                    DisplayName = "总市值",
                    MinValue = 5000000000m,  // 50亿元
                    MaxValue = 25000000000m  // 250亿元
                }
            ]
        };

        // Act
        var result = await service.ScreenAsync(criteria);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotEmpty(result, "市值50-250亿筛选应返回至少一只股票");
        Assert.IsLessThanOrEqualTo(criteria.Limit, result.Count, "返回数量不应超过 Limit");
        Assert.IsTrue(result.All(r => !string.IsNullOrWhiteSpace(r.Symbol)), "所有结果应包含有效代码");
        Assert.IsTrue(result.All(r => r.Mc >= 5_000_000_000m && r.Mc <= 25_000_000_000m),
            $"所有返回股票的市值应在 50-250 亿区间内，实际: {string.Join(", ", result.Select(r => $"{r.Symbol}={r.Mc}"))}");

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
            Criteria =
            [
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
            ]
        };

        // Act
        var result = await service.ScreenAsync(criteria);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotEmpty(result, "市值500亿-1万亿+PE 5-50 筛选应返回至少一只股票");
        Assert.IsLessThanOrEqualTo(criteria.Limit, result.Count, "返回数量不应超过 Limit");
        Assert.IsTrue(result.All(r => !string.IsNullOrWhiteSpace(r.Symbol)), "所有结果应包含有效代码");
        Assert.IsTrue(result.All(r => r.Mc >= 50_000_000_000m && r.Mc <= 1_000_000_000_000m),
            $"所有返回股票的市值应在 500 亿-1 万亿区间内，实际: {string.Join(", ", result.Select(r => $"{r.Symbol}={r.Mc}"))}");

        Console.WriteLine($"A股多条件筛选（市值+PE） - 返回股票数量: {result.Count}");
    }

    #endregion

    #region 虚拟币筛选测试

    [TestMethod]
    [TestCategory("Integration")]
    public async Task ScreenAsync_Crypto_WithDefaultCriteria_ShouldReturnCryptos()
    {
        // Arrange
        var service = _serviceProvider!.GetRequiredKeyedService<IAssetScreenerService>(MarketType.Crypto);
        var criteria = new CryptoCriteria
        {
            Limit = 10,
            Criteria = []
        };

        // Act
        var result = await service.ScreenAsync(criteria);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotEmpty(result, "虚拟币默认条件筛选应返回至少一条记录");
        Assert.IsLessThanOrEqualTo(criteria.Limit, result.Count, "返回数量不应超过 Limit");
        Assert.IsTrue(result.All(r => !string.IsNullOrEmpty(r.Symbol)), "所有结果应包含有效代码");

        Console.WriteLine($"虚拟币默认条件筛选 - 返回数量: {result.Count}");
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
            Criteria =
            [
                new()
                {
                    Code = "market_cap",
                    MinValue = 1_000_000_000m,  // 10亿美元
                    MaxValue = 50_000_000_000m  // 500亿美元
                }
            ]
        };

        // Act
        var result = await service.ScreenAsync(criteria);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotEmpty(result, "市值10-500亿美元筛选应返回至少一条记录");
        Assert.IsLessThanOrEqualTo(criteria.Limit, result.Count, "返回数量不应超过 Limit");
        Assert.IsTrue(result.All(r => !string.IsNullOrEmpty(r.Symbol)), "所有结果应包含有效代码");
        // CoinGecko 路径在客户端 ApplyFilters 中过滤，返回项市值应满足区间；
        // Binance 兜底路径 Mc=0，跳过市值校验
        if (result.Any(r => r.Mc > 0))
        {
            Assert.IsTrue(result.All(r => r.Mc >= 1_000_000_000m && r.Mc <= 50_000_000_000m),
                $"所有返回虚拟币的市值应在 10-500 亿美元区间内，实际: {string.Join(", ", result.Select(r => $"{r.Symbol}={r.Mc}"))}");
        }

        Console.WriteLine($"虚拟币市值筛选（10-500亿美元） - 返回数量: {result.Count}");
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
            Criteria =
            [
                new()
                {
                    Code = "price_change_24h",
                    MinValue = -10m,  // 跌幅不超过10%
                    MaxValue = 50m    // 涨幅50%以下
                }
            ]
        };

        // Act
        var result = await service.ScreenAsync(criteria);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotEmpty(result, "价格变化筛选应返回至少一条记录");
        Assert.IsLessThanOrEqualTo(criteria.Limit, result.Count, "返回数量不应超过 Limit");
        Assert.IsTrue(result.All(r => !string.IsNullOrEmpty(r.Symbol)), "所有结果应包含有效代码");
        Assert.IsTrue(result.All(r => r.Pct >= -10m && r.Pct <= 50m),
            $"所有返回虚拟币的 24h 涨跌幅应在 -10% ~ +50% 区间内，实际: {string.Join(", ", result.Select(r => $"{r.Symbol}={r.Pct}%"))}");

        Console.WriteLine($"虚拟币价格变化筛选（-10% ~ +50%） - 返回数量: {result.Count}");
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
            Criteria =
            [
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
            ]
        };

        // Act
        var result = await service.ScreenAsync(criteria);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotEmpty(result, "市值+交易量组合筛选应返回至少一条记录");
        Assert.IsLessThanOrEqualTo(criteria.Limit, result.Count, "返回数量不应超过 Limit");
        Assert.IsTrue(result.All(r => !string.IsNullOrEmpty(r.Symbol)), "所有结果应包含有效代码");
        if (result.Any(r => r.Mc > 0))
        {
            Assert.IsTrue(result.All(r => r.Mc >= 5_000_000_000m && r.Mc <= 100_000_000_000m),
                $"所有返回虚拟币的市值应在 50-1000 亿美元区间内，实际: {string.Join(", ", result.Select(r => $"{r.Symbol}={r.Mc}"))}");
        }
        Assert.IsTrue(result.All(r => r.Volume >= 100_000_000m),
            $"所有返回虚拟币的 24h 成交量应 >= 1 亿美元，实际: {string.Join(", ", result.Select(r => $"{r.Symbol}={r.Volume}"))}");

        Console.WriteLine($"虚拟币多条件筛选（市值+交易量） - 返回数量: {result.Count}");
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
            Criteria =
            [
                new()
                {
                    Code = "market_cap_rank",
                    MinValue = 1m,
                    MaxValue = 50m
                }
            ]
        };

        // Act
        var result = await service.ScreenAsync(criteria);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotEmpty(result, "市值排名前50筛选应返回至少一条记录");
        Assert.IsLessThanOrEqualTo(criteria.Limit, result.Count, "返回数量不应超过 Limit");
        Assert.IsTrue(result.All(r => !string.IsNullOrEmpty(r.Symbol)), "所有结果应包含有效代码");
        // CoinGecko 路径返回 MarketCapRank，验证排名在 1-50；Binance 兜底路径 MarketCapRank=0，跳过校验
        var cryptoResults = result.OfType<ScreenerCryptoInfo>().ToList();
        if (cryptoResults.Any(r => r.MarketCapRank > 0))
        {
            Assert.IsTrue(cryptoResults.All(r => r.MarketCapRank >= 1 && r.MarketCapRank <= 50),
                $"所有返回虚拟币的市值排名应在 1-50，实际: {string.Join(", ", cryptoResults.Select(r => $"{r.Symbol}={r.MarketCapRank}"))}");
        }

        Console.WriteLine($"虚拟币市值排名筛选（前50名） - 返回数量: {result.Count}");
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

    [TestMethod]
    [TestCategory("Unit")]
    public void DeterminePagination_WithClientSideFilters_ShouldFetchFullCandidatePool()
    {
        var criteria = new CryptoCriteria
        {
            Limit = 5,
            Criteria =
            [
                new() { Code = "market_cap", MaxValue = 1_000_000_000m }
            ]
        };

        var (page, perPage) = CryptoScreenerService.DeterminePagination(criteria);

        Assert.AreEqual(1, page);
        Assert.AreEqual(250, perPage, "带客户端筛选条件时应拉取 250 条候选池，避免仅在前 5 名大市值币中筛选导致结果为空");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DeterminePagination_WithoutFilters_ShouldStillFetchFullCandidatePool()
    {
        var criteria = new CryptoCriteria { Limit = IScreeningCriteria.CandidatePoolSize, Criteria = [] };

        var (page, perPage) = CryptoScreenerService.DeterminePagination(criteria);

        Assert.AreEqual(1, page);
        Assert.AreEqual(250, perPage, "无条件时也要拉满 250：剔除锚定型代币并与币安 USDT 现货求交后，只拉 Limit+10 条凑不满候选池");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ApplyFilters_ShouldExcludePeggedTokensOnly()
    {
        var markets = new List<CoinGeckoMarket>
        {
            CreateMarket("btc", 65000m, 1.2m, 8.5m),
            CreateMarket("usdt", 1.0003m, 0.02m, -0.05m),
            // 国债类 RWA 代币：旧的 10 符号清单识别不到
            CreateMarket("buidl", 1.0m, 0m, 0m),
            // 价格在 0.9–1.5 但 24h/30d 波动大，不是锚定型
            CreateMarket("xrp", 0.946m, -2.43m, -26.97m),
            CreateMarket("eth", 3200m, -0.5m, 3.1m)
        };
        var criteria = new CryptoCriteria { Limit = IScreeningCriteria.CandidatePoolSize, Criteria = [] };

        var filtered = CryptoScreenerService.ApplyFilters(markets, criteria);

        CollectionAssert.AreEqual(new[] { "btc", "xrp", "eth" }, filtered.Select(m => m.Symbol).ToArray());
    }

    [TestMethod]
    [TestCategory("Unit")]
    [DataRow(1.0, 0.0, 0.0, true, DisplayName = "BUIDL：价格 1、24h 0%、30d 0%")]
    [DataRow(1.0003, 0.02, -0.05, true, DisplayName = "USDT 级稳定币")]
    [DataRow(0.946, -2.43, -26.97, false, DisplayName = "XRP：价格在带内但 24h/30d 波动大")]
    [DataRow(1.0, 0.1, -3.5, false, DisplayName = "30d 波动达到 3%")]
    [DataRow(1.2, 1.5, 0.2, false, DisplayName = "24h 波动达到 1%")]
    [DataRow(0.5, 0.0, 0.0, false, DisplayName = "价格低于价格带")]
    [DataRow(65000.0, 0.1, 0.5, false, DisplayName = "BTC：价格远高于价格带")]
    public void IsPegged_ShouldRequirePriceBandAndLowVolatility(
        double price, double change24h, double change30d, bool expected)
    {
        var market = CreateMarket("x", (decimal)price, (decimal)change24h, (decimal)change30d);

        Assert.AreEqual(expected, CryptoScreenerService.IsPegged(market));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void IsPegged_WithoutThirtyDayChange_ShouldNotTreatAsPegged()
    {
        // 上市不足 30 天的币没有 30d 涨跌幅，无法确认锚定，不能当稳定币剔除
        var market = CreateMarket("new", 1.0m, 0m, null);

        Assert.IsFalse(CryptoScreenerService.IsPegged(market));
    }

    [TestMethod]
    [TestCategory("Unit")]
    [DataRow("market_cap")]
    [DataRow("market_cap_rank")]
    [DataRow("price_change_7d")]
    [DataRow("price_change_30d")]
    public void ApplyBinanceFilters_WithConditionBinanceCannotEvaluate_ShouldThrowFriendlyException(string code)
    {
        var tickers = new List<Binance24hrTicker>
        {
            new() { Symbol = "BTCUSDT", LastPrice = 65000m, QuoteVolume = 1_000_000_000m }
        };
        var criteria = new CryptoCriteria { Criteria = [new() { Code = code, MinValue = 1m }] };

        var exception = Assert.Throws<FriendlyException>(
            () => CryptoScreenerService.ApplyBinanceFilters(tickers, criteria));

        Assert.Contains(code, exception.Message);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ApplyBinanceFilters_WithSupportedConditions_ShouldFilterTickers()
    {
        var tickers = new List<Binance24hrTicker>
        {
            new() { Symbol = "BTCUSDT", LastPrice = 65000m, QuoteVolume = 1_000_000_000m, PriceChangePercent = 1.5m },
            new() { Symbol = "ETHUSDT", LastPrice = 3200m, QuoteVolume = 500_000_000m, PriceChangePercent = -6m },
            new() { Symbol = "DOGEUSDT", LastPrice = 0.12m, QuoteVolume = 900_000_000m, PriceChangePercent = 8m }
        };
        var criteria = new CryptoCriteria
        {
            Criteria =
            [
                new() { Code = "price_change_24h", MinValue = -10m, MaxValue = 5m },
                new() { Code = "volume_24h", MinValue = 600_000_000m }
            ]
        };

        var filtered = CryptoScreenerService.ApplyBinanceFilters(tickers, criteria);

        CollectionAssert.AreEqual(new[] { "BTCUSDT" }, filtered.Select(t => t.Symbol).ToArray());
    }

    private static CoinGeckoMarket CreateMarket(string symbol, decimal price, decimal? change24h, decimal? change30d) => new()
    {
        Symbol = symbol,
        Name = symbol,
        CurrentPrice = price,
        PriceChangePercentage24h = change24h,
        PriceChangePercentage30dInCurrency = change30d
    };

    #endregion
}

