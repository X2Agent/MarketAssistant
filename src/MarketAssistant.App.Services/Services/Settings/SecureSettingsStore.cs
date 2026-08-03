using System.Diagnostics;
using Microsoft.Identity.Client.Extensions.Msal;

namespace MarketAssistant.Services.Settings;

internal interface ISecureSettingsStore
{
    T? Read<T>();

    void Write<T>(T value);

    bool Exists();
}

internal sealed class SecureSettingsException : Exception
{
    public SecureSettingsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// 跨平台安全设置存储。Windows 使用 DPAPI，macOS 使用 Keychain，Linux 使用 Secret Service。
/// 不启用任何明文回退；平台安全存储不可用时调用方会收到明确异常。
/// </summary>
internal sealed class SecureSettingsStore : ISecureSettingsStore
{
    private const string MacKeyChainServiceName = "com.x2agent.marketassistant";
    private const string MacKeyChainAccountName = "MarketAssistant";
    private const string LinuxKeyringSchemaName = "com.x2agent.marketassistant";
    private const string LinuxKeyringCollection = "default";
    private const string LinuxKeyringSecretLabel = "MarketAssistant secure settings";

    private readonly Storage _storage;
    private readonly object _lock = new();

    public SecureSettingsStore(string fileName, string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var builder = new StorageCreationPropertiesBuilder(fileName, directory)
            .WithMacKeyChain(MacKeyChainServiceName, $"{MacKeyChainAccountName}:{fileName}")
            .WithLinuxKeyring(
                LinuxKeyringSchemaName,
                LinuxKeyringCollection,
                LinuxKeyringSecretLabel,
                new KeyValuePair<string, string>("Version", "1"),
                new KeyValuePair<string, string>("Store", fileName));

        _storage = Storage.Create(builder.Build(), new TraceSource(nameof(SecureSettingsStore)));
    }

    public T? Read<T>()
    {
        lock (_lock)
        {
            try
            {
                var data = _storage.ReadData();
                if (data is not { Length: > 0 })
                    return default;

                return JsonSerializer.Deserialize<T>(data);
            }
            catch (Exception ex) when (ex is not SecureSettingsException)
            {
                throw new SecureSettingsException("读取操作系统安全存储失败", ex);
            }
        }
    }

    public void Write<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        lock (_lock)
        {
            try
            {
                var data = JsonSerializer.SerializeToUtf8Bytes(value);
                _storage.WriteData(data);
            }
            catch (Exception ex) when (ex is not SecureSettingsException)
            {
                throw new SecureSettingsException("写入操作系统安全存储失败", ex);
            }
        }
    }

    public bool Exists()
    {
        lock (_lock)
        {
            try
            {
                return _storage.ReadData() is { Length: > 0 };
            }
            catch (Exception ex) when (ex is not SecureSettingsException)
            {
                throw new SecureSettingsException("访问操作系统安全存储失败", ex);
            }
        }
    }
}
