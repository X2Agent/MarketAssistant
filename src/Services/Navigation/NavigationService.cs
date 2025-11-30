using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.ViewModels;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Navigation;

/// <summary>
/// 导航项信息
/// </summary>
public class NavigationItem
{
    public ViewModelBase ViewModel { get; }
    public string? RootNavigationItemTitle { get; }
    public object? Parameter { get; }

    public NavigationItem(ViewModelBase viewModel, string? rootNavigationItemTitle = null, object? parameter = null)
    {
        ViewModel = viewModel;
        RootNavigationItemTitle = rootNavigationItemTitle;
        Parameter = parameter;
    }
}

/// <summary>
/// 导航服务，用于管理页面导航和导航栈
/// </summary>
public partial class NavigationService : ObservableObject, IRecipient<NavigationMessage>
{
    private readonly ILogger<NavigationService>? _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly Stack<NavigationItem> _navigationStack = new();
    private readonly Dictionary<string, Type> _routes = new();

    // 保持事件兼容性，但建议使用属性绑定
    public event EventHandler<NavigationItem>? Navigated;
    public event EventHandler? CanGoBackChanged;

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private ViewModelBase? _currentPage;

    [ObservableProperty]
    private string? _currentRootNavigationItemTitle;

    public NavigationService(IServiceProvider serviceProvider, ILogger<NavigationService>? logger = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        // 注册默认路由映射
        RegisterRoute<MCPConfigPageViewModel>("MCPConfig");
        RegisterRoute<StockPageViewModel>("Stock");
        RegisterRoute<AgentAnalysisViewModel>("Analysis");

        // 注册导航消息监听
        WeakReferenceMessenger.Default.Register(this);
    }

    /// <summary>
    /// 注册路由
    /// </summary>
    /// <typeparam name="TViewModel">ViewModel类型</typeparam>
    /// <param name="pageName">页面名称</param>
    private void RegisterRoute<TViewModel>(string pageName) where TViewModel : ViewModelBase
    {
        _routes[pageName] = typeof(TViewModel);
    }

    /// <summary>
    /// 接收导航消息
    /// </summary>
    public void Receive(NavigationMessage message)
    {
        if (_routes.TryGetValue(message.PageName, out var viewModelType))
        {
            var viewModel = (ViewModelBase)_serviceProvider.GetRequiredService(viewModelType);
            NavigateToInternal(viewModel, message.Parameter);
        }
        else
        {
            _logger?.LogWarning("未找到页面路由: {PageName}", message.PageName);
        }
    }

    /// <summary>
    /// 导航到指定类型的页面
    /// </summary>
    /// <typeparam name="TViewModel">ViewModel类型</typeparam>
    /// <param name="parameter">导航参数</param>
    public void NavigateTo<TViewModel>(object? parameter = null) where TViewModel : ViewModelBase
    {
        var viewModel = _serviceProvider.GetRequiredService<TViewModel>();
        NavigateToInternal(viewModel, parameter);
    }

    /// <summary>
    /// 内部导航实现
    /// </summary>
    private void NavigateToInternal(ViewModelBase viewModel, object? parameter, string? rootTitleOverride = null)
    {
        // 1. 如果当前有页面，通知它即将离开（被覆盖）
        if (_navigationStack.Count > 0)
        {
            var currentItem = _navigationStack.Peek();
            if (currentItem.ViewModel is INavigationAware currentAware)
            {
                currentAware.OnNavigatedFrom();
            }
        }

        // 如果已经有导航历史，使用当前的根导航项；或者使用覆盖值（用于NavigateToRoot）
        var rootNavigationItemTitle = rootTitleOverride ?? CurrentRootNavigationItemTitle;
        var navigationItem = new NavigationItem(viewModel, rootNavigationItemTitle, parameter);
        _navigationStack.Push(navigationItem);

        _logger?.LogInformation("导航到页面: {PageType}, 根导航项: {RootItem}",
            viewModel.GetType().Name, rootNavigationItemTitle ?? "无");

        // 2. 通知新页面进入
        if (viewModel is INavigationAware navigationAware)
        {
            navigationAware.OnNavigatedTo(parameter);
        }

        // 3. 更新状态（触发UI变更）
        UpdateState();

        Navigated?.Invoke(this, navigationItem);
        CanGoBackChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 返回上一页
    /// </summary>
    public bool GoBack()
    {
        if (!CanGoBack)
        {
            _logger?.LogWarning("无法返回，导航栈为空或只有一个页面");
            return false;
        }

        var poppedItem = _navigationStack.Pop();

        // 1. 通知被弹出的页面离开
        if (poppedItem.ViewModel is INavigationAware poppedAware)
        {
            poppedAware.OnNavigatedFrom();
        }

        // 释放旧页面资源
        if (poppedItem.ViewModel is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
                _logger?.LogDebug("已释放 ViewModel 资源: {Type}", poppedItem.ViewModel.GetType().Name);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "释放 ViewModel 资源时发生错误: {Type}", poppedItem.ViewModel.GetType().Name);
            }
        }

        if (_navigationStack.Count > 0)
        {
            var currentItem = _navigationStack.Peek();

            _logger?.LogInformation("返回到页面: {PageType}", currentItem.ViewModel.GetType().Name);

            // 2. 通知重新显示的页面（Re-activation）
            // 我们传递它原始的参数，以便它决定是否需要刷新
            if (currentItem.ViewModel is INavigationAware currentAware)
            {
                currentAware.OnNavigatedTo(currentItem.Parameter);
            }

            // 3. 更新状态（触发UI变更）
            UpdateState();

            Navigated?.Invoke(this, currentItem);
        }
        else
        {
            UpdateState();
        }

        CanGoBackChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// 清空导航栈并导航到指定页面（用于根级别导航）
    /// </summary>
    public void NavigateToRoot(ViewModelBase viewModel, string rootNavigationItemTitle)
    {
        // 清空并释放所有页面
        while (_navigationStack.Count > 0)
        {
            var item = _navigationStack.Pop();
            if (item.ViewModel is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "释放 ViewModel 资源时发生错误: {Type}", item.ViewModel.GetType().Name);
                }
            }
        }

        NavigateToInternal(viewModel, null, rootNavigationItemTitle);
    }

    /// <summary>
    /// 获取导航栈深度
    /// </summary>
    public int GetStackDepth() => _navigationStack.Count;

    /// <summary>
    /// 清空导航栈
    /// </summary>
    public void Clear()
    {
        while (_navigationStack.Count > 0)
        {
            var item = _navigationStack.Pop();
            if (item.ViewModel is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "释放 ViewModel 资源时发生错误: {Type}", item.ViewModel.GetType().Name);
                }
            }
        }

        UpdateState();
        CanGoBackChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateState()
    {
        CanGoBack = _navigationStack.Count > 1;
        if (_navigationStack.Count > 0)
        {
            var item = _navigationStack.Peek();
            CurrentPage = item.ViewModel;
            CurrentRootNavigationItemTitle = item.RootNavigationItemTitle;
        }
        else
        {
            CurrentPage = null;
            CurrentRootNavigationItemTitle = null;
        }
    }
}

