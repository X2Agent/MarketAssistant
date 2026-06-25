using MarketAssistant.Agents.Tools.Crypto;
using MarketAssistant.Applications.Charts;
using MarketAssistant.Applications.Charts.Models;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace TestMarketAssistant.Tools;

[TestClass]
public class CryptoTechnicalToolsTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public async Task TechnicalIndicators_ShouldReturnStructuredValues()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IKLineService, FakeKLineService>(MarketType.Crypto);

        using var serviceProvider = services.BuildServiceProvider();
        var tools = new CryptoTechnicalTools(
            NullLogger<CryptoTechnicalTools>.Instance,
            serviceProvider);

        var kdj = await tools.GetKDJAsync("BTC");
        var macd = await tools.GetMACDAsync("BTC");
        var boll = await tools.GetBOLLAsync("BTC");
        var ma = await tools.GetMAAsync("BTC");

        Assert.IsTrue(kdj.K.HasValue);
        Assert.IsTrue(kdj.D.HasValue);
        Assert.IsTrue(kdj.J.HasValue);

        Assert.AreNotEqual(0m, macd.Ema12);
        Assert.AreNotEqual(0m, macd.Ema26);

        Assert.IsTrue(boll.U.HasValue);
        Assert.IsTrue(boll.M.HasValue);
        Assert.IsTrue(boll.D.HasValue);
        Assert.IsTrue(boll.U > boll.M);
        Assert.IsTrue(boll.M > boll.D);

        Assert.IsTrue(ma.MA5.HasValue);
        Assert.IsTrue(ma.MA20.HasValue);
        Assert.IsTrue(ma.MA250.HasValue);
        Assert.IsTrue(ma.MA5 > ma.MA20);
    }

    private sealed class FakeKLineService : IKLineService
    {
        public Task<List<KLineData>> GetKLineDataAsync(string code, KLineType kLineType, int count = 100)
        {
            var start = new DateTime(2024, 1, 1);
            var data = Enumerable.Range(0, Math.Max(count, 260))
                .Select(index =>
                {
                    var basePrice = 100m + index;
                    return new KLineData
                    {
                        Timestamp = start.AddDays(index),
                        Open = basePrice,
                        High = basePrice + 2m,
                        Low = basePrice - 2m,
                        Close = basePrice + 1m,
                        Volume = 1_000m + index,
                        PreClose = basePrice - 1m,
                        Change = 2m,
                        PctChg = 1m,
                        Amount = (1_000m + index) * (basePrice + 1m)
                    };
                })
                .ToList();

            return Task.FromResult(data);
        }
    }
}
