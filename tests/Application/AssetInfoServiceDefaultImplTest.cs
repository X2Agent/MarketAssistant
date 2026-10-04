using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Infrastructure.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestMarketAssistant.Application;

/// <summary>
/// IAssetInfoService.GetAssetInfosAsync 默认实现单元测试（无需真实网络）
/// </summary>
[TestClass]
public class AssetInfoServiceDefaultImplTest
{
    /// <summary>
    /// 记录并发峰值的最小 Fake 实现（GetAssetInfoAsync 可按需抛异常）
    /// </summary>
    private sealed class FakeAssetInfoService(IEnumerable<string>? failingCodes = null) : IAssetInfoService
    {
        private readonly HashSet<string> _failingCodes = failingCodes?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        private int _current;

        public int MaxConcurrent { get; private set; }

        public Task<List<(string Name, string Code)>> SearchAsync(string keyword, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("本测试不涉及搜索");

        public async Task<AssetInfo> GetAssetInfoAsync(string code, string market = "", CancellationToken cancellationToken = default)
        {
            // 主动让出一次执行权，确保任务真正并发运行以测出峰值
            await Task.Yield();

            var current = Interlocked.Increment(ref _current);
            MaxConcurrent = Math.Max(MaxConcurrent, current);
            try
            {
                if (_failingCodes.Contains(code))
                {
                    throw new InvalidOperationException("模拟单项获取失败");
                }

                await Task.Delay(10, cancellationToken);
                return new AssetInfo { Code = code, Name = code, MarketType = MarketType.AShare };
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }

        public Task<List<HotAsset>> GetHotAssetsAsync()
            => throw new NotSupportedException("本测试不涉及热门资产");
    }

    private static FavoriteAsset CreateFavorite(string code) => new() { Code = code, Market = "SH" };

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetAssetInfosAsync_EmptyList_ReturnsEmpty()
    {
        var service = new FakeAssetInfoService();

        var results = await ((IAssetInfoService)service).GetAssetInfosAsync([]);

        Assert.IsNotNull(results);
        Assert.IsEmpty(results);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetAssetInfosAsync_SingleItemFailure_ReturnsNullForThatItem()
    {
        // Arrange：第二项抛异常，其余成功
        var service = new FakeAssetInfoService(["SH600002"]);
        var favorites = new List<FavoriteAsset>
        {
            CreateFavorite("SH600001"),
            CreateFavorite("SH600002"),
            CreateFavorite("SH600003")
        };

        // Act
        var results = await ((IAssetInfoService)service).GetAssetInfosAsync(favorites);

        // Assert：等长同序，仅失败项为 null
        Assert.HasCount(3, results);
        Assert.IsNotNull(results[0]);
        Assert.IsNull(results[1]);
        Assert.IsNotNull(results[2]);
        Assert.AreEqual("SH600001", results[0]!.Code);
        Assert.AreEqual("SH600003", results[2]!.Code);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetAssetInfosAsync_ConcurrencyLimitedToThree()
    {
        // Arrange：10 个全部成功的请求
        var service = new FakeAssetInfoService();
        var favorites = Enumerable.Range(1, 10).Select(i => CreateFavorite($"SH6000{i:00}")).ToList();

        // Act
        var results = await ((IAssetInfoService)service).GetAssetInfosAsync(favorites);

        // Assert：全部返回且并发峰值不超过 3
        Assert.HasCount(10, results);
        Assert.IsTrue(results.All(r => r != null));
        Assert.IsLessThanOrEqualTo(3, service.MaxConcurrent, $"并发峰值应不超过3，实际为 {service.MaxConcurrent}");
    }
}
