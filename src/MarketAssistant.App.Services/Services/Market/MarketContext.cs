using System.ComponentModel;
using System.Runtime.CompilerServices;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace MarketAssistant.Services.Market;

/// <summary>
/// 市场切换事件参数
/// </summary>
public class MarketChangedEventArgs : EventArgs
{
    /// <summary>切换前的市场</summary>
    public MarketType PreviousMarket { get; }

    /// <summary>切换后的市场</summary>
    public MarketType NewMarket { get; }

    public MarketChangedEventArgs(MarketType previousMarket, MarketType newMarket)
    {
        PreviousMarket = previousMarket;
        NewMarket = newMarket;
    }
}

/// <summary>
/// 市场上下文服务，管理当前激活的市场类型
/// </summary>
public class MarketContext : INotifyPropertyChanged
{
    private readonly IUserSettingService _userSettingService;
    private readonly IServiceProvider _serviceProvider;
    private readonly object _marketLock = new();
    // volatile 保证无锁读取 CurrentMarket 时能立即看到其他线程的切换结果
    private volatile MarketType _currentMarket;

    /// <summary>
    /// 当前激活的市场类型（静态快照，供 UI Converter 等无法直接依赖注入的场景读取）
    /// </summary>
    private static volatile MarketType s_currentMarketType;

    public static MarketType CurrentMarketType => s_currentMarketType;

    /// <summary>
    /// 当前激活的市场类型
    /// </summary>
    public MarketType CurrentMarket => _currentMarket;

    public MarketContext(IUserSettingService userSettingService, IServiceProvider serviceProvider)
    {
        _userSettingService = userSettingService;
        _serviceProvider = serviceProvider;
        _currentMarket = _userSettingService.CurrentSetting.CurrentMarketType;
        s_currentMarketType = _currentMarket;
    }

    /// <summary>
    /// 获取当前市场的能力声明
    /// </summary>
    public IMarketCapability CurrentCapability => GetService<IMarketCapability>();

    /// <summary>
    /// 解析当前市场的 Keyed 服务（统一入口，替代已废弃的 MarketServiceRegistry 门面）
    /// </summary>
    public T GetService<T>() where T : class =>
        _serviceProvider.GetRequiredKeyedService<T>(CurrentMarket);

    /// <summary>
    /// 解析指定市场的 Keyed 服务
    /// </summary>
    public T GetService<T>(MarketType marketType) where T : class =>
        _serviceProvider.GetRequiredKeyedService<T>(marketType);

    /// <summary>
    /// 市场切换事件，供后端服务订阅以清理状态或暂停后台任务。
    /// 与 PropertyChanged 不同，此事件携带前后市场信息，且语义明确。
    /// </summary>
    public event EventHandler<MarketChangedEventArgs>? MarketChanged;

    /// <summary>
    /// 切换市场
    /// </summary>
    /// <param name="newMarket">新的市场类型</param>
    public void SwitchMarket(MarketType newMarket)
    {
        MarketType previousMarket;
        lock (_marketLock)
        {
            if (_currentMarket == newMarket)
                return;
            previousMarket = _currentMarket;
            _currentMarket = newMarket;
            s_currentMarketType = newMarket;
        }

        // 与持久化共用同步边界，避免与其它线程的设置保存交错
        _userSettingService.UpdateSetting(setting => setting.CurrentMarketType = newMarket);
        OnPropertyChanged(nameof(CurrentMarket));
        MarketChanged?.Invoke(this, new MarketChangedEventArgs(previousMarket, newMarket));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}


