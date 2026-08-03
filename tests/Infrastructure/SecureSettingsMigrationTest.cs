using System.Text.Json;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Settings;

namespace TestMarketAssistant.Infrastructure;

[TestClass]
public sealed class SecureSettingsMigrationTest
{
    [TestMethod]
    public void UserSettings_ExistingSecureValues_ShouldWinAndRemoveLegacyPlaintext()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "usersettings.json");
        File.WriteAllText(settingsPath, """
            {
              "ProviderId": "DeepSeek",
              "ModelId": "deepseek-v4-flash",
              "ProviderApiKeys": { "DeepSeek": "legacy-secret" }
            }
            """);
        var store = new InMemorySecureSettingsStore();
        store.Seed(new
        {
            ProviderApiKeys = new Dictionary<string, string> { ["DeepSeek"] = "secure-secret" },
            EmbeddingApiKey = "",
            ZhiTuApiToken = "",
            CoinGeckoApiKey = "",
            BinanceApiKey = "",
            BinanceSecretKey = "",
            WebSearchApiKey = ""
        });

        var service = new UserSettingService(settingsPath, store);

        Assert.AreEqual("secure-secret", service.CurrentSetting.ProviderApiKeys["DeepSeek"]);
        var sanitizedJson = File.ReadAllText(settingsPath);
        Assert.IsFalse(sanitizedJson.Contains("legacy-secret", StringComparison.Ordinal));
        Assert.IsFalse(sanitizedJson.Contains("ProviderApiKeys", StringComparison.Ordinal));
    }

    [TestMethod]
    public void UserSettings_Reset_ShouldOverwriteStoredSecretsWithEmptyValues()
    {
        using var directory = new TemporaryDirectory();
        var store = new InMemorySecureSettingsStore();
        var service = new UserSettingService(Path.Combine(directory.Path, "usersettings.json"), store);
        service.CurrentSetting.ProviderApiKeys["DeepSeek"] = "secret";
        service.CurrentSetting.BinanceSecretKey = "binance-secret";
        service.SaveSettings();

        service.ResetSettings();

        Assert.IsFalse(store.Json.Contains("secret", StringComparison.Ordinal));
        using var document = JsonDocument.Parse(store.Json);
        Assert.AreEqual(0, document.RootElement.GetProperty("ProviderApiKeys").GetRawText().Length - 2);
        Assert.AreEqual(string.Empty, document.RootElement.GetProperty("BinanceSecretKey").GetString());
    }

    [TestMethod]
    public void UserSettings_SecureWriteFailure_ShouldNotWriteOrdinarySettings()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "usersettings.json");
        File.WriteAllText(settingsPath, "original");
        var store = new InMemorySecureSettingsStore { FailWrites = true };
        var service = new UserSettingService(settingsPath, store);
        service.CurrentSetting.ProviderId = "DeepSeek";

        Assert.ThrowsExactly<SecureSettingsException>(() => service.SaveSettings());
        Assert.AreEqual("original", File.ReadAllText(settingsPath));
        Assert.IsFalse(File.Exists(settingsPath + ".tmp"));
    }

    [TestMethod]
    public async Task UserSettings_MultipleInstances_ShouldSerializeWritesForSamePath()
    {
        using var directory = new TemporaryDirectory();
        var settingsPath = Path.Combine(directory.Path, "usersettings.json");
        var first = new UserSettingService(settingsPath, new InMemorySecureSettingsStore());
        var second = new UserSettingService(settingsPath, new InMemorySecureSettingsStore());

        var firstWrite = Task.Run(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                first.CurrentSetting.ModelId = $"first-{i}";
                first.SaveSettings();
            }
        });
        var secondWrite = Task.Run(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                second.CurrentSetting.ModelId = $"second-{i}";
                second.SaveSettings();
            }
        });

        await Task.WhenAll(firstWrite, secondWrite);

        Assert.IsFalse(File.Exists(settingsPath + ".tmp"));
        using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
        Assert.IsTrue(document.RootElement.GetProperty("ModelId").GetString() is { Length: > 0 });
    }

    [TestMethod]
    public async Task McpSettings_MultipleInstances_ShouldSerializeWritesForSamePath()
    {
        using var directory = new TemporaryDirectory();
        var configPath = Path.Combine(directory.Path, "mcpservers.json");
        var first = new MCPServerConfigService(configPath, new InMemorySecureSettingsStore());
        var second = new MCPServerConfigService(configPath, new InMemorySecureSettingsStore());

        var firstWrite = Task.Run(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                first.AddOrUpdateConfig(new MCPServerConfig
                {
                    Id = "first",
                    Name = $"first-{i}"
                });
            }
        });
        var secondWrite = Task.Run(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                second.AddOrUpdateConfig(new MCPServerConfig
                {
                    Id = "second",
                    Name = $"second-{i}"
                });
            }
        });

        await Task.WhenAll(firstWrite, secondWrite);

        Assert.IsFalse(File.Exists(configPath + ".tmp"));
        using var document = JsonDocument.Parse(File.ReadAllText(configPath));
        Assert.AreEqual(1, document.RootElement.GetArrayLength());
    }

    [TestMethod]
    public void McpSettings_ExistingSecureValues_ShouldWinAndRemoveLegacyPlaintext()
    {
        using var directory = new TemporaryDirectory();
        var configPath = Path.Combine(directory.Path, "mcpservers.json");
        File.WriteAllText(configPath, """
            [
              {
                "Id": "server-1",
                "Name": "test",
                "TransportType": "stdio",
                "Command": "tool",
                "EnvironmentVariables": { "TOKEN": "legacy-token" }
              }
            ]
            """);
        var store = new InMemorySecureSettingsStore();
        store.Seed(new Dictionary<string, Dictionary<string, string?>>
        {
            ["server-1"] = new() { ["TOKEN"] = "secure-token" }
        });

        var service = new MCPServerConfigService(configPath, store);

        Assert.AreEqual("secure-token", service.GetConfig("server-1")!.EnvironmentVariables["TOKEN"]);
        var sanitizedJson = File.ReadAllText(configPath);
        Assert.IsFalse(sanitizedJson.Contains("legacy-token", StringComparison.Ordinal));
        Assert.IsFalse(sanitizedJson.Contains("EnvironmentVariables", StringComparison.Ordinal));
    }

    private sealed class InMemorySecureSettingsStore : ISecureSettingsStore
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

    private sealed class TemporaryDirectory : IDisposable
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
}
