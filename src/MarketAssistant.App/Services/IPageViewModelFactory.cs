using MarketAssistant.ViewModels;

namespace MarketAssistant.Services;

/// <summary>
/// 页面 ViewModel 工厂：导航触发时才实例化页面，替代散落的 Func 委托与 IServiceProvider 服务定位。
/// </summary>
public interface IPageViewModelFactory
{
    T Create<T>() where T : ViewModelBase;

    ViewModelBase Create(Type viewModelType);
}
