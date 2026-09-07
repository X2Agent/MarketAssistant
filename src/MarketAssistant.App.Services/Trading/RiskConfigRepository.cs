using System.Text.Json;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 风控配置仓储（internal）：风控配置 JSON 的加载与保存。
/// </summary>
internal sealed class RiskConfigRepository : TradingRepositoryBase
{
    public RiskConfigRepository(
        TradingSchemaInitializer schema,
        TradingEnvironmentService environment,
        Microsoft.Extensions.Logging.ILogger logger)
        : base(schema, environment, logger)
    {
    }

    public async Task<RiskConfig> LoadRiskConfigAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT config_json FROM risk_config WHERE environment = @environment AND market_type = @marketType";
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@marketType", (int)MarketType.Crypto);

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is not string json || string.IsNullOrEmpty(json))
            return new RiskConfig();
        try
        {
            return JsonSerializer.Deserialize<RiskConfig>(json) ?? new RiskConfig();
        }
        catch (JsonException ex)
        {
            Logger.LogWarning(ex, "风控配置反序列化失败，将使用默认配置");
            return new RiskConfig();
        }
    }

    public async Task SaveRiskConfigAsync(RiskConfig config, CancellationToken ct = default)
    {
        await EnsureInitializedAsync();
        await using var conn = await OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO risk_config (environment, market_type, config_json, updated_at)
            VALUES (@environment, @marketType, @configJson, @updatedAt)
            ON CONFLICT(environment, market_type) DO UPDATE SET config_json = @configJson, updated_at = @updatedAt
            """;
        cmd.Parameters.AddWithValue("@environment", CurrentEnvironmentKey);
        cmd.Parameters.AddWithValue("@marketType", (int)MarketType.Crypto);
        cmd.Parameters.AddWithValue("@configJson", JsonSerializer.Serialize(config));
        cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
