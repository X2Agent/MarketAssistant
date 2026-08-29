using MarketAssistant.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MarketAssistant.Services;

/// <summary>
/// 基于 DI 容器的页面 ViewModel 工厂实现，页面 ViewModel 以 Transient 注册，每次导航创建新实例。
/// </summary>
public sealed class PageViewModelFactory : IPageViewModelFactory
{
    private readonly IServiceProvider _serviceProvider;

    public PageViewModelFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public T Create<T>() where T : ViewModelBase
        => _serviceProvider.GetRequiredService<T>();

    public ViewModelBase Create(Type viewModelType)
        => (ViewModelBase)_serviceProvider.GetRequiredService(viewModelType);
}
