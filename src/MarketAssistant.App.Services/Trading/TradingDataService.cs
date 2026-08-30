using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 交易数据持久化门面，管理策略、交易记录、持仓、日统计、账户快照与风控配置的 SQLite 存储。
/// 对外保持原有公开契约（含 virtual 测试替换点）；内部按聚合根拆分为
/// <see cref="TradingStrategyRepository"/>/<see cref="TradeRecordRepository"/>/<see cref="PositionRepository"/>/
/// <see cref="DailyStatsRepository"/>/<see cref="AccountSnapshotRepository"/>/<see cref="RiskConfigRepository"/>，
/// schema 初始化统一由 <see cref="TradingSchemaInitializer"/> 负责。
/// </summary>
public class TradingDataService
{
    private readonly TradingSchemaInitializer _schema;
    private readonly TradingStrategyRepository _strategies;
    private readonly PositionRepository _positions;
    private readonly DailyStatsRepository _dailyStats;
    private readonly AccountSnapshotRepository _accountSnapshots;
    private readonly RiskConfigRepository _riskConfig;
    private readonly TradeRecordRepository _records;

    public TradingDataService(
        TradingEnvironmentService tradingEnvironmentService,
        ILogger<TradingDataService> logger)
    {
        _schema = new TradingSchemaInitializer(logger);
        _strategies = new TradingStrategyRepository(_schema, tradingEnvironmentService, logger);
        _positions = new PositionRepository(_schema, tradingEnvironmentService, logger);
        _dailyStats = new DailyStatsRepository(_schema, tradingEnvironmentService, logger);
        _accountSnapshots = new AccountSnapshotRepository(_schema, tradingEnvironmentService, logger);
        _riskConfig = new RiskConfigRepository(_schema, tradingEnvironmentService, logger);
        _records = new TradeRecordRepository(_schema, tradingEnvironmentService, logger, _positions, _dailyStats);
    }

    // ─────────────────────────── 策略 ───────────────────────────

    public Task SaveStrategyAsync(TradingStrategy strategy, CancellationToken ct = default)
        => _strategies.SaveStrategyAsync(strategy, ct);

    public Task<TradingStrategy?> GetStrategyAsync(string id, CancellationToken ct = default)
        => _strategies.GetStrategyAsync(id, ct);

    public Task<List<TradingStrategy>> GetStrategiesByStatusAsync(StrategyStatus status, CancellationToken ct = default)
        => _strategies.GetStrategiesByStatusAsync(status, ct);

    public Task<List<TradingStrategy>> GetAllStrategiesAsync(CancellationToken ct = default)
        => _strategies.GetAllStrategiesAsync(ct);

    public Task UpdateStrategyStatusAsync(string id, StrategyStatus status, CancellationToken ct = default)
        => _strategies.UpdateStrategyStatusAsync(id, status, ct);

    public Task DeleteStrategyAsync(string id, CancellationToken ct = default)
        => _strategies.DeleteStrategyAsync(id, ct);

    public Task UpdateStrategyTriggeredAsync(string id, CancellationToken ct = default)
        => _strategies.UpdateStrategyTriggeredAsync(id, ct);

    /// <remarks>virtual 供单元测试替换。</remarks>
    public virtual Task UpdateStrategyLastTriggeredAtAsync(string id, CancellationToken ct = default)
        => _strategies.UpdateStrategyLastTriggeredAtAsync(id, ct);

    public Task UpdateStrategyTriggeredWithParamsAsync(string id, string? customParams, CancellationToken ct = default)
        => _strategies.UpdateStrategyTriggeredWithParamsAsync(id, customParams, ct);

    /// <remarks>virtual 供单元测试替换。</remarks>
    public virtual Task UpdateStrategyCustomParamsAsync(string id, string? customParams, CancellationToken ct = default)
        => _strategies.UpdateStrategyCustomParamsAsync(id, customParams, ct);

    public Task UpdateStrategyGuardrailsAsync(
        string id, decimal? stopLossPrice, decimal? takeProfitPrice, CancellationToken ct = default)
        => _strategies.UpdateStrategyGuardrailsAsync(id, stopLossPrice, takeProfitPrice, ct);

    /// <remarks>virtual 供单元测试替换。</remarks>
    public virtual Task UpdateStrategyTrailingPeakAsync(string id, decimal? trailingPeakPrice, CancellationToken ct = default)
        => _strategies.UpdateStrategyTrailingPeakAsync(id, trailingPeakPrice, ct);

    // ─────────────────────────── 交易记录 ───────────────────────────

    public virtual Task SaveTradeRecordAsync(TradeRecord record, CancellationToken ct = default)
        => _records.SaveTradeRecordAsync(record, ct);

    public Task<List<TradeRecord>> GetTradeRecordsAsync(
        string? symbol = null, DateTime? from = null, DateTime? to = null, int limit = 50,
        CancellationToken ct = default)
        => _records.GetTradeRecordsAsync(symbol, from, to, limit, ct);

    public Task<List<TradeRecord>> GetRecordsByStrategyAsync(string strategyId, CancellationToken ct = default)
        => _records.GetRecordsByStrategyAsync(strategyId, ct);

    public Task<List<TradeRecord>> GetUnsettledTradeRecordsAsync(
        string? symbol = null, CancellationToken ct = default)
        => _records.GetUnsettledTradeRecordsAsync(symbol, ct);

    public Task<TradeRecord> ReconcileTradeRecordAsync(
        TradeRecord existingRecord, ExchangeOrderResult latestOrder, CancellationToken ct = default)
        => _records.ReconcileTradeRecordAsync(existingRecord, latestOrder, ct);

    // ─────────────────────────── 日统计 ───────────────────────────

    public virtual Task<DailyStats> GetTodayStatsAsync(CancellationToken ct = default)
        => _dailyStats.GetTodayStatsAsync(ct);

    public virtual Task UpdateDailyStatsAsync(decimal pnl, decimal commission, bool countTrade = true, CancellationToken ct = default)
        => _dailyStats.UpdateDailyStatsAsync(pnl, commission, countTrade, ct);

    // ─────────────────────────── 持仓 ───────────────────────────

    public Task OpenPositionAsync(Position position, CancellationToken ct = default)
        => _positions.OpenPositionAsync(position, ct);

    public virtual Task<decimal> ClosePositionFifoAsync(
        string symbol, decimal closeQty, decimal closePrice,
        CancellationToken ct = default, PositionSide side = PositionSide.Long)
        => _positions.ClosePositionFifoAsync(symbol, closeQty, closePrice, ct, side);

    public virtual Task<List<Position>> GetOpenPositionsAsync(string? symbol = null, CancellationToken ct = default)
        => _positions.GetOpenPositionsAsync(symbol, ct);

    /// <remarks>virtual 供单元测试替换。</remarks>
    public virtual Task<decimal> GetOpenPositionAvgEntryPriceAsync(string symbol, CancellationToken ct = default)
        => _positions.GetOpenPositionAvgEntryPriceAsync(symbol, ct);

    // ─────────────────────────── 账户快照 ───────────────────────────

    public Task SaveAccountSnapshotAsync(decimal totalValueUsdt, CancellationToken ct = default)
        => _accountSnapshots.SaveAccountSnapshotAsync(totalValueUsdt, ct);

    public Task<decimal> GetPeakAccountValueAsync(DateTime? since = null, CancellationToken ct = default)
        => _accountSnapshots.GetPeakAccountValueAsync(since, ct);

    // ─────────────────────────── 风控配置 ───────────────────────────

    public virtual Task<RiskConfig> LoadRiskConfigAsync(CancellationToken ct = default)
        => _riskConfig.LoadRiskConfigAsync(ct);

    public Task SaveRiskConfigAsync(RiskConfig config, CancellationToken ct = default)
        => _riskConfig.SaveRiskConfigAsync(config, ct);

    /// <summary>
    /// 在同一事务内完成建表、旧结构迁移和索引创建（供单元测试直接驱动 schema 迁移）。
    /// </summary>
    internal static Task MigrateDatabaseSchemaAsync(SqliteConnection conn)
        => TradingSchemaInitializer.MigrateDatabaseSchemaAsync(conn);
}
