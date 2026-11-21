using System.Text.Json;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Settings;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Trading;

[TestClass]
public sealed class RiskConfigRepositoryTest
{
    private string _dbPath = null!;

    [TestInitialize]
    public void TestInitialize()
        => _dbPath = Path.Combine(Path.GetTempPath(), $"market-assistant-risk-{Guid.NewGuid():N}.db");

    [TestCleanup]
    public void TestCleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LoadRiskConfig_RecordMissing_ShouldReturnDefaults()
    {
        var repository = CreateRepository();

        var config = await repository.LoadRiskConfigAsync();

        Assert.AreEqual(10, config.FuturesLeverage);
        Assert.IsFalse(config.RequireLivePromotion);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LoadRiskConfig_CorruptedJson_ShouldThrowInsteadOfDefaults()
    {
        var repository = CreateRepository();
        await repository.LoadRiskConfigAsync();
        await using var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO risk_config (environment, market_type, config_json, updated_at)
            VALUES ($environment, $marketType, $json, $updatedAt)
            """;
        command.Parameters.AddWithValue("$environment", TradingEnvironmentKeys.LiveSpot);
        command.Parameters.AddWithValue("$marketType", (int)MarketType.Crypto);
        command.Parameters.AddWithValue("$json", "{broken");
        command.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();

        await Assert.ThrowsExactlyAsync<JsonException>(() => repository.LoadRiskConfigAsync());
    }

    private RiskConfigRepository CreateRepository()
    {
        var settingService = new Mock<IUserSettingService>();
        settingService.SetupGet(s => s.CurrentSetting)
            .Returns(new UserSetting { CryptoTradingMode = TradingMode.LiveSpot });
        var environment = new TradingEnvironmentService(
            settingService.Object,
            Mock.Of<IMarketMonitorProvider>(),
            NullLogger<TradingEnvironmentService>.Instance);
        var schema = new TradingSchemaInitializer(NullLogger.Instance, _dbPath);
        return new RiskConfigRepository(schema, environment, NullLogger.Instance);
    }
}
