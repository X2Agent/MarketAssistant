using System.ComponentModel;
using System.Runtime.CompilerServices;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace MarketAssistant.Services.Market;

/// <summary>
/// 市场上下文服务，管理当前激活的市场类型
/// </summary>
public class MarketContext : INotifyPropertyChanged
{
    private readonly IUserSettingService _userSettingService;
    private readonly IServiceProvider _serviceProvider;
    private readonly object _marketLock = new();
    private MarketType _currentMarket;

    /// <summary>
    /// 当前激活的市场类型（静态快照，供 UI Converter 等无法直接依赖注入的场景读取）
    /// </summary>
    public static MarketType CurrentMarketType { get; private set; }

    /// <summary>
    /// 当前激活的市场类型
    /// </summary>
    public MarketType CurrentMarket => _currentMarket;

    public MarketContext(IUserSettingService userSettingService, IServiceProvider serviceProvider)
    {
        _userSettingService = userSettingService;
        _serviceProvider = serviceProvider;
        _currentMarket = _userSettingService.CurrentSetting.CurrentMarketType;
        CurrentMarketType = _currentMarket;
    }

    /// <summary>
    /// 获取当前市场的能力声明
    /// </summary>
    public IMarketCapability CurrentCapability =>
        _serviceProvider.GetRequiredKeyedService<IMarketCapability>(CurrentMarket);

    /// <summary>
    /// 切换市场
    /// </summary>
    /// <param name="newMarket">新的市场类型</param>
    public void SwitchMarket(MarketType newMarket)
    {
        lock (_marketLock)
        {
            if (_currentMarket == newMarket)
                return;
            _currentMarket = newMarket;
            CurrentMarketType = newMarket;
        }

        _userSettingService.CurrentSetting.CurrentMarketType = newMarket;
        _userSettingService.SaveSettings();
        OnPropertyChanged(nameof(CurrentMarket));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}


