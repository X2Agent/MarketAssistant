using System.Globalization;
using MarketAssistant.Trading.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 交易仓储基类（internal）：封装环境 key 解析、decimal 存取辅助、连接获取与延迟初始化。
/// 各仓储继承本类，仅关注自身的 CRUD SQL；schema 初始化统一由 <see cref="TradingSchemaInitializer"/> 负责。
/// </summary>
internal abstract class TradingRepositoryBase(
    TradingSchemaInitializer schema,
    TradingEnvironmentService environment,
    ILogger logger)
{
    private readonly TradingSchemaInitializer _schema = schema;
    private readonly TradingEnvironmentService _environment = environment;

    protected ILogger Logger { get; } = logger;

    /// <summary>
    /// 4 种交易模式各自独立的环境 key，确保现货实盘、现货 Demo、合约实盘、合约 Testnet
    /// 的策略、交易记录、持仓、风控配置互不混淆。
    /// </summary>
    protected string CurrentEnvironmentKey => TradingEnvironmentKeys.FromMode(_environment.CurrentMode);

    /// <summary>
    /// 当前是否为合约模式（合约买卖方向需结合持仓判断开平仓）
    /// </summary>
    protected bool IsFuturesMode => _environment.CurrentMode is
        TradingMode.LiveFutures or TradingMode.FuturesTestnet;

    protected Task EnsureInitializedAsync() => _schema.EnsureSchemaInitializedAsync();

    protected Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        => _schema.CreateConnectionAsync(cancellationToken);

    /// <summary>
    /// 金额/数量列以 TEXT（InvariantCulture 十进制字符串）存储，
    /// 避免 REAL（double）存储对高精度小数（如 8 位小数价格）造成精度损失。
    /// </summary>
    protected static object ToDb(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc cref="ToDb(decimal)"/>
    protected static object ToDbNullable(decimal? value) => value.HasValue ? ToDb(value.Value) : DBNull.Value;

    /// <summary>
    /// 读取金额/数量列，兼容 TEXT（新格式）与 REAL/INTEGER（历史数据）两种存储形态。
    /// </summary>
    protected static decimal ReadDecimal(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
            return 0;

        return reader.GetValue(ordinal) switch
        {
            string s when decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            double d => (decimal)d,
            long l => l,
            _ => 0
        };
    }
}
