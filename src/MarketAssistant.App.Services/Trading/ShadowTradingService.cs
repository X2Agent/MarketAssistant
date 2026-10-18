using System.Globalization;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Trading.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 影子交易流水行（内存视图）。数据库形态见 TradingSchemaInitializer 的 shadow_trades 表。
/// </summary>
public sealed record ShadowTrade(
    string StrategyId,
    string Symbol,
    OrderSide Side,
    decimal Quantity,
    decimal Price,
    decimal Fee,
    decimal RealizedPnl,
    decimal ClosedQuantity,
    DateTime CreatedAt);

/// <summary>
/// 影子交易账本：影子模式（不下单）下的模拟成交与 FIFO 已实现盈亏。
/// </summary>
/// <remarks>
/// 没有账本的"影子模式"只能回答"AI 会怎么想"，不能回答"这个策略到底赚不赚"。
/// 本服务按与实盘一致的口径记账：开仓记零盈亏，平仓按 FIFO 结算并扣除手续费，
/// 使影子绩效可以与实盘绩效直接对比。
/// </remarks>
public sealed class ShadowTradingService(
    TradingDataService dataService,
    ILogger<ShadowTradingService> logger)
{
    /// <summary>模拟手续费率（币安现货 taker 0.1%）。仅影响记账，不下单。</summary>
    public const decimal DefaultFeeRate = 0.001m;

    /// <summary>
    /// 记录一笔影子成交。若存在相反方向的未平仓行（卖出平多 / 买入平空），
    /// 则按 FIFO 结算已实现盈亏并写回该行，同时标记本行已匹配数量以防平仓行被再次当作开仓行。
    /// </summary>
    public async Task<decimal> RecordAsync(
        string strategyId, string symbol, OrderSide side, decimal quantity, decimal price,
        decimal feeRate = DefaultFeeRate, CancellationToken ct = default)
    {
        if (quantity <= 0 || price <= 0)
            return 0m;

        var fee = quantity * price * feeRate;
        var oppositeSide = side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
        var (realizedPnl, matchedQuantity) = await CloseFifoAsync(
            strategyId, symbol, oppositeSide, quantity, price, fee, ct).ConfigureAwait(false);

        await InsertAsync(new ShadowTrade(
            strategyId, symbol, side, quantity, price, fee, realizedPnl, matchedQuantity, DateTime.UtcNow), ct)
            .ConfigureAwait(false);

        if (realizedPnl != 0)
        {
            logger.LogInformation(
                "影子平仓结算: {StrategyId} {Symbol} 数量 {Qty} 价 {Price} 盈亏 {Pnl}",
                strategyId, symbol, quantity, price, realizedPnl);
        }

        return realizedPnl;
    }

    /// <summary>按 FIFO 用相反方向的开仓行结算本次平仓的已实现盈亏（含双边手续费）。</summary>
    private async Task<(decimal RealizedPnl, decimal MatchedQuantity)> CloseFifoAsync(
        string strategyId, string symbol, OrderSide entrySide, decimal closeQuantity, decimal closePrice, decimal closeFee,
        CancellationToken ct)
    {
        var openEntries = await LoadOpenPositionsAsync(strategyId, symbol, entrySide, ct).ConfigureAwait(false);

        var remaining = closeQuantity;
        var pnl = 0m;
        var totalMatched = 0m;

        foreach (var entry in openEntries)
        {
            if (remaining <= 0)
                break;

            var available = entry.Quantity - entry.ClosedQuantity;
            if (available <= 0)
                continue;

            var matched = Math.Min(available, remaining);
            // 多头入场（Buy）卖出平仓：(平仓价 - 开仓价) × 数量；空头入场（Sell）买入平仓：(开仓价 - 平仓价) × 数量
            var gross = entrySide == OrderSide.Buy
                ? (closePrice - entry.Price) * matched
                : (entry.Price - closePrice) * matched;
            // 手续费按本次匹配数量占原成交量的比例分摊
            var matchedFee = entry.Fee * (matched / entry.Quantity) + closeFee * (matched / closeQuantity);
            pnl += gross - matchedFee;

            await UpdateClosedQuantityAsync(entry, entry.ClosedQuantity + matched, ct).ConfigureAwait(false);
            remaining -= matched;
            totalMatched += matched;
        }

        // 没有足够开仓行可结算（如首次建仓或手动平仓）时盈亏为 0
        return (pnl, totalMatched);
    }

    /// <summary>查询某策略某标的指定方向尚未平完的开仓行（按时间正序，FIFO）。</summary>
    public async Task<List<ShadowTrade>> LoadOpenPositionsAsync(
        string strategyId, string symbol, OrderSide entrySide, CancellationToken ct = default)
    {
        var rows = await LoadAsync(strategyId, symbol, ct).ConfigureAwait(false);
        return [.. rows
            .Where(row => row.Side == entrySide && row.Quantity - row.ClosedQuantity > 0)
            .OrderBy(row => row.CreatedAt)];
    }

    /// <summary>查询某策略某标的尚未平完的多头开仓行（按时间正序，FIFO）。</summary>
    public Task<List<ShadowTrade>> LoadOpenBuysAsync(
        string strategyId, string symbol, CancellationToken ct = default)
        => LoadOpenPositionsAsync(strategyId, symbol, OrderSide.Buy, ct);

    /// <summary>查询影子流水（按环境、策略/标的过滤）。</summary>
    public async Task<List<ShadowTrade>> LoadAsync(
        string? strategyId = null, string? symbol = null, CancellationToken ct = default)
    {
        var conditions = new List<string> { "environment = @environment" };
        var parameters = new List<SqliteParameter>
        {
            new("@environment", dataService.CurrentEnvironmentKey)
        };

        if (!string.IsNullOrEmpty(strategyId))
        {
            conditions.Add("strategy_id = @strategyId");
            parameters.Add(new SqliteParameter("@strategyId", strategyId));
        }

        if (!string.IsNullOrEmpty(symbol))
        {
            conditions.Add("symbol = @symbol");
            parameters.Add(new SqliteParameter("@symbol", symbol));
        }

        var where = conditions.Count == 0 ? string.Empty : $" WHERE {string.Join(" AND ", conditions)}";

        var rows = new List<ShadowTrade>();
        await using var conn = await dataService.OpenShadowConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT strategy_id, symbol, side, quantity, price, fee, pnl, closed_quantity, created_at
            FROM shadow_trades{where}
            ORDER BY created_at
            """;
        cmd.Parameters.AddRange([.. parameters]);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new ShadowTrade(
                reader.GetString(0),
                reader.GetString(1),
                (OrderSide)reader.GetInt32(2),
                decimal.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                decimal.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                decimal.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                decimal.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
                decimal.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
                DateTime.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return rows;
    }

    private async Task InsertAsync(ShadowTrade trade, CancellationToken ct)
    {
        await using var conn = await dataService.OpenShadowConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO shadow_trades (id, environment, strategy_id, symbol, side, quantity, price, fee, pnl, closed_quantity, created_at)
            VALUES (@id, @environment, @strategyId, @symbol, @side, @quantity, @price, @fee, @pnl, @closed, @createdAt)
            """;
        cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("@environment", dataService.CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@strategyId", trade.StrategyId);
        cmd.Parameters.AddWithValue("@symbol", trade.Symbol);
        cmd.Parameters.AddWithValue("@side", (int)trade.Side);
        cmd.Parameters.AddWithValue("@quantity", trade.Quantity.ToString(CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@price", trade.Price.ToString(CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@fee", trade.Fee.ToString(CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@pnl", trade.RealizedPnl.ToString(CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@closed", trade.ClosedQuantity.ToString(CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@createdAt", trade.CreatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task UpdateClosedQuantityAsync(ShadowTrade buy, decimal closedQuantity, CancellationToken ct)
    {
        // shadow_trades 无自然业务键，用 (环境, 策略, 标的, 方向, 价格, 创建时间) 精确定位行
        await using var conn = await dataService.OpenShadowConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE shadow_trades SET closed_quantity = @closed
            WHERE environment = @environment AND strategy_id = @strategyId AND symbol = @symbol AND side = @side
              AND price = @price AND created_at = @createdAt
            """;
        cmd.Parameters.AddWithValue("@closed", closedQuantity.ToString(CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@environment", dataService.CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@strategyId", buy.StrategyId);
        cmd.Parameters.AddWithValue("@symbol", buy.Symbol);
        cmd.Parameters.AddWithValue("@side", (int)buy.Side);
        cmd.Parameters.AddWithValue("@price", buy.Price.ToString(CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@createdAt", buy.CreatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
