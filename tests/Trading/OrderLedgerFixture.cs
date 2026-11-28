using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Settings;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Trading;

internal sealed class OrderLedgerFixture : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"order-ledger-{Guid.NewGuid():N}.db");
    internal PositionRepository Positions { get; }
    internal TradeRecordRepository Records { get; }

    internal OrderLedgerFixture(Mock<TradingDataService> data, bool futures = false)
    {
        var settings = new Mock<IUserSettingService>();
        settings.SetupGet(s => s.CurrentSetting).Returns(new UserSetting { CryptoTradingMode = futures ? TradingMode.FuturesTestnet : TradingMode.SpotDemo });
        var env = new TradingEnvironmentService(settings.Object, Mock.Of<IMarketMonitorProvider>(), NullLogger<TradingEnvironmentService>.Instance);
        var schema = new TradingSchemaInitializer(NullLogger.Instance, _path);
        Positions = new PositionRepository(schema, env, NullLogger.Instance);
        Records = new TradeRecordRepository(schema, env, NullLogger.Instance, Positions, new DailyStatsRepository(schema, env, NullLogger.Instance));
        data.Setup(d => d.GetUnsettledTradeRecordsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).Returns(Records.GetUnsettledTradeRecordsAsync);
        data.Setup(d => d.SaveTradeRecordAsync(It.IsAny<TradeRecord>(), It.IsAny<CancellationToken>())).Returns(Records.SaveTradeRecordAsync);
        data.Setup(d => d.ReconcileTradeRecordAsync(It.IsAny<TradeRecord>(), It.IsAny<ExchangeOrderResult>(), It.IsAny<CancellationToken>())).Returns(Records.ReconcileTradeRecordAsync);
    }

    public void Dispose()
    {
        using var conn = new SqliteConnection($"Data Source={_path}");
        SqliteConnection.ClearPool(conn);
        File.Delete(_path);
    }
}
