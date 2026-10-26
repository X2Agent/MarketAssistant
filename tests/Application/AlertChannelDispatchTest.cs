using System.Globalization;
using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.Applications.AlertCenter.Channels;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Notification;
using MarketAssistant.Services.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace TestMarketAssistant.Application;

/// <summary>
/// 告警触达通道测试：通道按最低级别过滤，Critical 穿透免打扰，
/// 单通道异常不影响桌面通知与其它通道。
/// </summary>
[TestClass]
public sealed class AlertChannelDispatchTest
{
    private const string TestSymbol = "CHANNELTEST";
    private string _dbPath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"alert_channel_test_{Guid.NewGuid():N}.db");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (string.IsNullOrEmpty(_dbPath))
            return;

        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        SqliteConnection.ClearPool(conn);
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private sealed class RecordingChannel : IAlertChannel
    {
        public string Name => "测试通道";
        public AlertLevel MinimumLevel { get; init; } = AlertLevel.Info;
        public List<AlertEvent> Received { get; } = [];
        public bool ThrowOnSend { get; init; }

        public Task SendAsync(AlertEvent alert, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSend)
                throw new InvalidOperationException("通道故障");

            Received.Add(alert);
            return Task.CompletedTask;
        }
    }

    private static AlertEvent CreateAlert(AlertLevel level, string title = "测试告警") => new()
    {
        MarketType = MarketType.Crypto,
        Symbol = TestSymbol,
        Level = level,
        Source = AlertSource.Risk,
        Title = title,
        Content = "内容"
    };

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Channel_ShouldReceiveAlertsAtOrAboveMinimumLevel()
    {
        var channel = new RecordingChannel { MinimumLevel = AlertLevel.Warning };
        var service = CreateService(channel);

        await service.RaiseAlertAsync(CreateAlert(AlertLevel.Info, "提示级告警"));
        await service.RaiseAlertAsync(CreateAlert(AlertLevel.Warning, "警告级告警"));
        await service.RaiseAlertAsync(CreateAlert(AlertLevel.Critical, "紧急级告警"));

        Assert.HasCount(2, channel.Received, "低于最低级别的告警不应推送到通道");
        Assert.IsTrue(channel.Received.All(alert => alert.Level >= AlertLevel.Warning));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task CriticalAlert_ShouldReachChannelEvenInQuietHours()
    {
        var settings = new UserSetting
        {
            Notification = true,
            AlertQuietHoursEnabled = true,
            AlertQuietStartHour = 0,
            AlertQuietEndHour = 23
        };
        var channel = new RecordingChannel();
        var service = CreateService(channel, settings);

        await service.RaiseAlertAsync(CreateAlert(AlertLevel.Critical));

        Assert.HasCount(1, channel.Received, "紧急告警必须穿透免打扰时段");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task ChannelFailure_ShouldNotBreakAlertPipeline()
    {
        var healthy = new RecordingChannel();
        var broken = new RecordingChannel { ThrowOnSend = true };
        var service = CreateService(broken, channels: [healthy]);

        // 故障通道不得让 RaiseAlertAsync 抛出，也不得影响健康通道
        await service.RaiseAlertAsync(CreateAlert(AlertLevel.Critical));

        Assert.HasCount(1, healthy.Received, "其它通道应正常收到告警");
        Assert.AreEqual(1, await CountTestAlertsAsync(), "告警仍应正常落库");
    }

    private async Task<int> CountTestAlertsAsync()
    {
        await using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM alert_events WHERE symbol = @symbol";
        cmd.Parameters.AddWithValue("@symbol", TestSymbol);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private AlertCenterService CreateService(
        IAlertChannel channel,
        UserSetting? settings = null,
        IReadOnlyList<IAlertChannel>? channels = null)
    {
        var notification = new StubNotificationService();
        var userSettings = new StubUserSettingService(settings ?? new UserSetting { Notification = true });

        return new AlertCenterService(
            notification,
            userSettings,
            new AlertGate(),
            channels ?? [channel],
            NullLogger<AlertCenterService>.Instance,
            _dbPath);
    }

    private sealed class StubNotificationService : INotificationService
    {
        public List<string> Messages { get; } = [];

        public void ShowSuccess(string message, int durationMs = 3000) => Messages.Add(message);
        public void ShowError(string message, int durationMs = 3000) => Messages.Add(message);
        public void ShowInfo(string message, int durationMs = 3000) => Messages.Add(message);
        public void ShowWarning(string message, int durationMs = 3000) => Messages.Add(message);
    }

    private sealed class StubUserSettingService(UserSetting setting) : IUserSettingService
    {
        public UserSetting CurrentSetting { get; } = setting;

        public void LoadSettings() { }
        public void SaveSettings() { }
        public void UpdateSettings(UserSetting setting) { }
        public void UpdateSetting(Action<UserSetting> mutate) => mutate(CurrentSetting);
        public void ResetSettings() { }
    }
}
