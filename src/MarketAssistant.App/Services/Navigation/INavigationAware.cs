namespace MarketAssistant.Services.Navigation;

/// <summary>
/// 导航感知接口
/// 实现此接口的 ViewModel 可以在导航发生时接收通知和参数
/// </summary>
public interface INavigationAware
{
    /// <summary>
    /// 当导航到此页面时调用（首次进入或 GoBack 重新激活均会触发）。
    /// 默认实现转发到 <see cref="OnNavigatedTo(object?, bool)"/>。
    /// </summary>
    /// <param name="parameter">导航参数</param>
    void OnNavigatedTo(object? parameter) => OnNavigatedTo(parameter, isReactivation: false);

    /// <summary>
    /// 当导航到此页面时调用，可区分首次进入与 GoBack 重新激活。
    /// 实现者可据此决定是否重新执行副作用（如重新订阅、重新加载）。
    /// </summary>
    /// <param name="parameter">导航参数</param>
    /// <param name="isReactivation">true 表示从子页面 GoBack 重新激活；false 表示首次进入</param>
    void OnNavigatedTo(object? parameter, bool isReactivation);

    /// <summary>
    /// 当从此页面离开时调用
    /// </summary>
    void OnNavigatedFrom();
}

/// <summary>
/// 泛型导航感知接口，提供强类型参数支持
/// </summary>
/// <typeparam name="T">参数类型</typeparam>
public interface INavigationAware<T> : INavigationAware
{
    /// <summary>
    /// 当导航到此页面时调用（强类型，首次进入或 GoBack 重新激活均会触发）
    /// 默认实现转发到 <see cref="OnNavigatedTo(T, bool)"/>（isReactivation = false）。
    /// </summary>
    /// <param name="parameter">强类型参数</param>
    void OnNavigatedTo(T parameter) => OnNavigatedTo(parameter, isReactivation: false);

    /// <summary>
    /// 当导航到此页面时调用（强类型，可区分首次进入与重新激活）
    /// </summary>
    /// <param name="parameter">强类型参数</param>
    /// <param name="isReactivation">true 表示从子页面 GoBack 重新激活；false 表示首次进入</param>
    void OnNavigatedTo(T parameter, bool isReactivation) => OnNavigatedTo(parameter);

    // 显式实现基接口方法，进行类型转换
    void INavigationAware.OnNavigatedTo(object? parameter, bool isReactivation)
    {
        if (parameter is T t)
        {
            OnNavigatedTo(t, isReactivation);
        }
        // 如果参数为null且T允许null，也调用
        else if (parameter is null && default(T) is null)
        {
            OnNavigatedTo(default!, isReactivation);
        }
        // 类型不匹配时忽略
    }
}
