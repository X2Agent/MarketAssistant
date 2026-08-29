using MarketAssistant.Applications.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Infrastructure.Core;

/// <summary>
/// SQLite 服务基类，封装通用的连接字符串构建、连接打开（WAL 模式 + busy_timeout）
/// 以及延迟初始化（含重试）逻辑，供各 SQLite 持久化服务继承使用。
/// 所有子类共享统一的 market.db 文件。
/// </summary>
public abstract class SqliteServiceBase
{
    /// <summary>
    /// 统一数据库文件名。所有持久化服务共享此文件。
    /// </summary>
    protected internal const string UnifiedDbFileName = "market.db";

    private readonly object _initLock = new();
    private Task? _initializeTask;

    /// <summary>
    /// SQLite 连接字符串（格式：Data Source={dbPath}）
    /// </summary>
    protected string ConnectionString { get; }

    /// <summary>
    /// 日志记录器
    /// </summary>
    protected ILogger Logger { get; }

    /// <summary>
    /// 构造函数，使用统一数据库文件 market.db 构建连接字符串并创建数据目录。
    /// </summary>
    /// <param name="logger">日志记录器</param>
    protected SqliteServiceBase(ILogger logger)
    {
        Logger = logger;

        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppInfo.AppName);
        Directory.CreateDirectory(appDataDir);

        var dbPath = Path.Combine(appDataDir, UnifiedDbFileName);
        // 统一使用 Data Source={dbPath}，不指定 Mode=ReadWriteCreate
        // 文件不存在时由 SqliteConnection.OpenAsync 自动创建
        ConnectionString = $"Data Source={dbPath}";
    }

    /// <summary>
    /// 打开一个新的 SQLite 连接，并统一启用 WAL 模式和 busy_timeout。
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>已打开的 SqliteConnection（调用方负责释放）</returns>
    protected async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(cancellationToken);

        await using var pragmaCmd = conn.CreateCommand();
        // WAL 模式：读写不互斥，提升并发性能
        // busy_timeout：写冲突时等待 5 秒而非立即抛 "database is locked"
        pragmaCmd.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA busy_timeout=5000;
            """;
        await pragmaCmd.ExecuteNonQueryAsync(cancellationToken);

        return conn;
    }

    /// <summary>
    /// 延迟初始化数据库，失败后允许重试（避免 Task 处于 Faulted 状态导致后续调用永久失败）。
    /// </summary>
    /// <param name="initializeFunc">初始化数据库的委托（通常是 InitializeDatabaseAsync）</param>
    protected async Task EnsureInitializedAsync(Func<Task> initializeFunc)
    {
        if (_initializeTask != null && _initializeTask.IsCompletedSuccessfully)
            return;

        lock (_initLock)
        {
            // 如果之前失败或被取消，重置允许重试（取消的任务 await 会永久抛 TaskCanceledException）
            if (_initializeTask is { IsFaulted: true } or { IsCanceled: true })
                _initializeTask = null;

            _initializeTask ??= initializeFunc();
        }
        await _initializeTask;
    }

    /// <summary>
    /// 初始化数据库表结构，由子类实现各服务特有的建表 SQL。
    /// </summary>
    protected abstract Task InitializeDatabaseAsync();
}
