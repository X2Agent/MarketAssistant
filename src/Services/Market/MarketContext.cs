using System.ComponentModel;
using System.Runtime.CompilerServices;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Settings;

namespace MarketAssistant.Services.Market;

/// <summary>
/// 市场上下文服务，管理当前激活的市场类型
/// </summary>
public class MarketContext : INotifyPropertyChanged
{
    private readonly IUserSettingService _userSettingService;
    private MarketType _currentMarket;

    /// <summary>
    /// 当前激活的市场类型
    /// </summary>
    public MarketType CurrentMarket
    {
        get => _currentMarket;
        private set
        {
            if (_currentMarket != value)
            {
                _currentMarket = value;
                OnPropertyChanged();
            }
        }
    }

    public MarketContext(IUserSettingService userSettingService)
    {
        _userSettingService = userSettingService;
        
        // 从用户设置中加载市场类型
        _currentMarket = _userSettingService.CurrentSetting.CurrentMarketType;
    }

    /// <summary>
    /// 切换市场
    /// </summary>
    /// <param name="newMarket">新的市场类型</param>
    public void SwitchMarket(MarketType newMarket)
    {
        if (CurrentMarket != newMarket)
        {
            CurrentMarket = newMarket;
            
            // 保存到用户设置
            _userSettingService.CurrentSetting.CurrentMarketType = newMarket;
            _userSettingService.SaveSettings();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}






