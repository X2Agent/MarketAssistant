namespace MarketAssistant.Services.Navigation;

/// <summary>
/// 导航感知接口
/// 实现此接口的 ViewModel 可以在导航发生时接收通知和参数
/// </summary>
public interface INavigationAware
{
    /// <summary>
    /// 当导航到此页面时调用
    /// </summary>
    /// <param name="parameter">导航参数</param>
    void OnNavigatedTo(object? parameter);

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
    /// 当导航到此页面时调用（强类型）
    /// </summary>
    /// <param name="parameter">强类型参数</param>
    void OnNavigatedTo(T parameter);

    // 显式实现基接口方法，进行类型转换
    void INavigationAware.OnNavigatedTo(object? parameter)
    {
        if (parameter is T t)
        {
            OnNavigatedTo(t);
        }
        // 如果参数为null且T允许null，也调用
        else if (parameter is null && default(T) is null)
        {
            OnNavigatedTo(default!);
        }
        // 类型不匹配时忽略
    }
}
