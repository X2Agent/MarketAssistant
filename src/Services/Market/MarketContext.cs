using System.ComponentModel;
using System.Runtime.CompilerServices;
using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Services.Market;

/// <summary>
/// 市场上下文服务，管理当前激活的市场类型
/// </summary>
public class MarketContext : INotifyPropertyChanged
{
    private MarketType _currentMarket = MarketType.AShare;

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

    /// <summary>
    /// 切换市场
    /// </summary>
    /// <param name="newMarket">新的市场类型</param>
    public void SwitchMarket(MarketType newMarket)
    {
        if (CurrentMarket != newMarket)
        {
            CurrentMarket = newMarket;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}






