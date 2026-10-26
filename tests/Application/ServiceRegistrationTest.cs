using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.Briefing;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Applications.Telegrams;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services;
using MarketAssistant.Services.Settings;
using MarketAssistant.ViewModels;
using MarketAssistant.ViewModels.Home;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;

namespace TestMarketAssistant.Application;

/// <summary>
/// DI 注册冒烟测试：以应用真实启动顺序（<c>Program.ConfigureServices</c>）构建容器，
/// 确保新增依赖（简报开关、简报 ViewModel）能被解析。
/// </summary>
/// <remarks>
/// 价值在于 fail-fast：应用本身开启了 <c>ValidateOnBuild</c>，任何缺失注册都会在
/// 用户启动时炸掉而非编译期暴露。本测试把该失败前移到 CI。
/// 顺序必须与 Program 保持一致：AddApplicationServices → 覆盖设置服务 → AddViewModels。
/// </remarks>
[TestClass]
public sealed class ServiceRegistrationTest
{
    /// <summary>
    /// 复刻 <c>Program.ConfigureServices</c> 的注册序列。
    /// 刻意不实例化真实 <see cref="UserSettingService"/>：其构造函数会读操作系统安全存储，
    /// 在无 keyring 的 CI 环境会抛 SecureSettingsException。此处只验证依赖图，不触碰 IO。
    /// </summary>
    /// <param name="stubBriefingService">
    /// 为 true 时用内存桩替换 keyed 的 <see cref="IDailyBriefingService"/>：
    /// 简报 ViewModel 测试绝不能触达真实 <c>market.db</c>，否则在当天生成过简报的
    /// 机器上缓存命中，"无缓存"断言必挂。
    /// </param>
    /// <param name="briefingEnabled">桩服务的总开关取值，驱动开关门禁分支的测试。</param>
    private static ServiceProvider BuildAppContainer(bool stubBriefingService = false, bool briefingEnabled = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IUserSettingService>(new StubUserSettingService());
        services.AddApplicationServices();

        // 与 Program 一致：业务层注册后再用启动期实例覆盖
        services.RemoveAll<IUserSettingService>();
        services.AddSingleton<IUserSettingService>(new StubUserSettingService());

        if (stubBriefingService)
        {
            services.RemoveAll<IDailyBriefingService>();
            services.AddKeyedSingleton<IDailyBriefingService>(MarketType.AShare, new StubBriefingService(briefingEnabled));
            services.AddKeyedSingleton<IDailyBriefingService>(MarketType.Crypto, new StubBriefingService(briefingEnabled));
        }

        services.AddViewModels();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AppContainer_WithValidateOnBuild_ShouldNotThrow()
    {
        using var provider = BuildAppContainer();
        Assert.IsNotNull(provider);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DailyBriefingService_ShouldResolveForBothMarkets()
    {
        using var provider = BuildAppContainer();

        // S3：简报服务按市场 Keyed 注册，两侧都必须可解析
        var aShare = provider.GetRequiredKeyedService<IDailyBriefingService>(MarketType.AShare);
        var crypto = provider.GetRequiredKeyedService<IDailyBriefingService>(MarketType.Crypto);

        Assert.IsNotNull(aShare);
        Assert.IsNotNull(crypto);
        Assert.AreNotSame(aShare, crypto, "两个市场必须是各自独立的实例（数据源不同）");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DailyBriefingViewModel_ShouldResolveFromContainer()
    {
        // 简报 VM 依赖市场上下文 / 窗口服务 / 设置服务，
        // 此处确保构造签名与注册一致，避免启动期才发现缺依赖。
        using var provider = BuildAppContainer(stubBriefingService: true);

        var viewModel = provider.GetRequiredService<DailyBriefingViewModel>();
        Assert.IsNotNull(viewModel);
    }

    /// <summary>
    /// 简报 ViewModel 必须是单例：它持有市场切换订阅与当天缓存读取，
    /// 按 Transient 注册会导致每次进入首页都重复读库、重复订阅市场事件。
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    public void DailyBriefingViewModel_ShouldBeSingleton()
    {
        using var provider = BuildAppContainer(stubBriefingService: true);

        var first = provider.GetRequiredService<DailyBriefingViewModel>();
        var second = provider.GetRequiredService<DailyBriefingViewModel>();

        Assert.AreSame(
            first, second,
            "简报 ViewModel 必须复用同一实例，避免重复读库与重复订阅市场事件");
    }


    /// <summary>轮询等待简报 ViewModel 结束生成态（含超时兜底，避免测试悬挂）。</summary>
    private static async Task WaitUntilIdleAsync(DailyBriefingViewModel viewModel)
    {
        for (var i = 0; i < 100 && viewModel.IsGenerating; i++)
        {
            await Task.Delay(50);
        }

        // 再留一拍，确保 finally 中的 IsGenerating=false 已生效
        await Task.Delay(50);
    }

    /// <summary>
    /// 轮询等待构造触发的后台缓存读取结束：State 离开 Idle/Loading 进入终态。
    /// VM 构造函数 fire-and-forget 调用 <c>LoadCachedAsync</c>，直接断言会与后台任务竞态。
    /// </summary>
    private static async Task WaitUntilBriefingLoadedAsync(DailyBriefingViewModel viewModel)
    {
        for (var i = 0; i < 100 && viewModel.State is LoadState.Idle or LoadState.Loading; i++)
        {
            await Task.Delay(50);
        }
    }

    /// <summary>
    /// 纯手动策略回归：构造简报 ViewModel 不得触发任何模型调用。
    /// </summary>
    /// <remarks>
    /// 这是一条<b>成本护栏</b>：简报每次生成约 500+ tokens，若恢复"进首页自动生成"，
    /// 用户会在毫不知情的情况下被持续计费。构造函数只允许读缓存（以内存桩隔离真实库）。
    /// </remarks>
    [TestMethod]
    [TestCategory("Unit")]
    public async Task BriefingViewModel_ShouldNotAutoGenerateOnConstruction()
    {
        using var provider = BuildAppContainer(stubBriefingService: true);
        var viewModel = provider.GetRequiredService<DailyBriefingViewModel>();

        // 构造即触发 LoadCachedAsync，等待其结束后断言：未进入生成态
        await WaitUntilBriefingLoadedAsync(viewModel);
        await WaitUntilIdleAsync(viewModel);

        Assert.IsFalse(viewModel.IsGenerating, "构造完成后不应仍在生成中");
        Assert.IsFalse(
            viewModel.HasBriefing,
            "无当日缓存时不应凭空产生简报（说明发生了自动生成）");
        Assert.AreEqual(
            LoadState.Empty,
            viewModel.State,
            "无缓存且未生成时应为空态，等待用户显式点击生成");
        Assert.IsTrue(viewModel.IsBriefingEnabled, "默认设置下开关快照应为开启");
    }

    /// <summary>
    /// 总开关关闭时：生成入口被门禁拦截，即使绕过按钮禁用直接调用也不得触达服务生成方法。
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    public async Task BriefingViewModel_ShouldBlockGenerateWhenSwitchOff()
    {
        using var provider = BuildAppContainer(stubBriefingService: true, briefingEnabled: false);
        var viewModel = provider.GetRequiredService<DailyBriefingViewModel>();
        await WaitUntilBriefingLoadedAsync(viewModel);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.IsFalse(viewModel.HasBriefing, "开关关闭时不得生成简报");
        Assert.IsFalse(viewModel.IsGenerating, "开关关闭时不得进入生成态");
        StringAssert.Contains(viewModel.EmptyMessage, "已关闭",
            "空态文案须指明功能已关闭，引导用户去设置开启");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void BriefingSwitch_DefaultOn_And_ReflectsUserSetting()
    {
        var defaults = new UserSetting();
        Assert.IsTrue(defaults.EnableDailyBriefing, "默认开启，升级用户不丢失留存钩子");

        // 直接验证真实服务属性，而非在测试里复刻布尔表达式
        var on = BuildBriefingService(enable: true);
        Assert.IsTrue(on.IsEnabled, "总开关开启时服务应报告可用");

        var off = BuildBriefingService(enable: false);
        Assert.IsFalse(off.IsEnabled, "总开关关闭时服务应报告不可用");
    }

    /// <summary>
    /// 用真实 <see cref="DailyBriefingService"/> 校验开关读取。
    /// 其余依赖（自选/要闻/模型客户端）用桩注入，开关判定不触达它们。
    /// </summary>
    private static IDailyBriefingService BuildBriefingService(bool enable)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IFavoriteService, NoopFavoriteService>(MarketType.AShare);
        services.AddKeyedSingleton<ITelegramService, NoopTelegramService>(MarketType.AShare);

        using var provider = services.BuildServiceProvider();

        return new DailyBriefingService(
            MarketType.AShare,
            provider,
            new StubChatClientFactory(),
            new StubUserSettingService(enable),
            NullLogger<DailyBriefingService>.Instance);
    }

    private sealed class StubUserSettingService(bool enable = true) : IUserSettingService
    {
        public UserSetting CurrentSetting { get; } = new()
        {
            EnableDailyBriefing = enable
        };

        public void LoadSettings() { }

        public void SaveSettings() { }

        public void UpdateSettings(UserSetting setting) { }

        public void UpdateSetting(Action<UserSetting> mutate) => mutate(CurrentSetting);

        public void ResetSettings() { }
    }

    private sealed class NoopFavoriteService : IFavoriteService
    {
        public Task AddFavoriteAsync(string code, string market, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task RemoveFavoriteAsync(string code, string market, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> IsFavoriteAsync(string code, string market, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<List<FavoriteAsset>> GetFavoritesCodesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<FavoriteAsset>());

        public Task<List<AssetInfo>> GetFavoritesWithLatestDataAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<AssetInfo>());

        public Task ClearFavoritesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoopTelegramService : ITelegramService
    {
        public Task<List<Telegram>> GetTelegraphsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<Telegram>());
    }

    /// <summary>
    /// 简报服务内存桩：只提供无缓存的确定性行为，让 ViewModel 测试与真实 SQLite 隔离。
    /// </summary>
    private sealed class StubBriefingService(bool enabled = true) : IDailyBriefingService
    {
        public bool IsEnabled => enabled;

        public Task<DailyBriefing?> GetTodayAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<DailyBriefing?>(null);

        public Task<DailyBriefing?> GenerateAsync(bool force = false, CancellationToken cancellationToken = default)
            => Task.FromResult<DailyBriefing?>(null);

        public Task<DailyBriefingUsageSummary> GetUsageSummaryAsync(int days = 30, CancellationToken cancellationToken = default)
            => Task.FromResult(new DailyBriefingUsageSummary(0, 0, 0, false));
    }

    private sealed class StubChatClientFactory : IChatClientFactory
    {
        public IChatClient CreateClient() => new NoopChatClient();

        public ChatClientRuntime CreateRuntime(string? modelIdOverride = null)
            => new(new NoopChatClient(), "stub", "stub-model", "https://stub.local", "fp",
                StructuredOutputMode.Text);

        public void Dispose() { }
    }

    private sealed class NoopChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, "noop")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
