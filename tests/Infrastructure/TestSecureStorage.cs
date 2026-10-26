using System.Text.Json;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Settings;

namespace TestMarketAssistant.Infrastructure;

/// <summary>
/// 内存版安全设置存储。单元测试一律不得触碰操作系统钥匙串：Linux 缺少 libsecret 原生库时
/// <see cref="SecureSettingsStore"/> 会直接抛出 <see cref="DllNotFoundException"/>（CI 上的真实失败），
/// 而测试关心的是迁移与并发语义，不是各平台的安全存储实现。
/// </summary>
internal sealed class InMemorySecureSettingsStore : ISecureSettingsStore
{
    public bool FailWrites { get; init; }

    public string Json { get; private set; } = string.Empty;

    public bool Exists() => Json.Length > 0;

    public T? Read<T>() => Json.Length == 0 ? default : JsonSerializer.Deserialize<T>(Json);

    public void Write<T>(T value)
    {
        if (FailWrites)
            throw new SecureSettingsException("simulated", new InvalidOperationException());

        Json = JsonSerializer.Serialize(value);
    }

    public void Seed<T>(T value)
    {
        Json = JsonSerializer.Serialize(value);
    }
}

/// <summary>
/// 测试期间独占的临时目录，<see cref="Dispose"/> 时递归删除。
/// 用于把配置服务从真实的 <c>AppDataDirectory</c> 上摘下来，避免测试读写开发者本机配置。
/// </summary>
internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "MarketAssistantTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}

/// <summary>
/// 隔离的配置服务工厂：临时目录 + 内存安全存储，构造过程不会访问系统钥匙串与本机配置。
/// </summary>
internal static class TestConfigServices
{
    public static MCPServerConfigService CreateMcpServerConfigService(TemporaryDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        return new MCPServerConfigService(
            System.IO.Path.Combine(directory.Path, "mcpservers.json"),
            new InMemorySecureSettingsStore());
    }
}
