using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.ViewModels;
using MarketAssistant.ViewModels.Trading;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Navigation;

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

public partial class NavigationService : ObservableObject, IRecipient<NavigationMessage>
{
    private readonly ILogger<NavigationService>? _logger;
    private readonly IPageViewModelFactory _pageViewModelFactory;
    private readonly Stack<NavigationItem> _navigationStack = new();
    private readonly Dictionary<string, Type> _routes = new();

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private ViewModelBase? _currentPage;

    [ObservableProperty]
    private string? _currentRootNavigationItemTitle;

    public NavigationService(IPageViewModelFactory pageViewModelFactory, ILogger<NavigationService>? logger = null)
    {
        _pageViewModelFactory = pageViewModelFactory;
        _logger = logger;

        RegisterRoute<MCPConfigPageViewModel>("MCPConfig");
        RegisterRoute<AssetPageViewModel>("Asset");
        RegisterRoute<AgentAnalysisViewModel>("Analysis");
        RegisterRoute<BalanceDetailPageViewModel>("BalanceDetail");

        WeakReferenceMessenger.Default.Register(this);
    }

    private void RegisterRoute<TViewModel>(string pageName) where TViewModel : ViewModelBase
    {
        _routes[pageName] = typeof(TViewModel);
    }

    public void Receive(NavigationMessage message)
    {
        if (_routes.TryGetValue(message.PageName, out var viewModelType))
        {
            var viewModel = _pageViewModelFactory.Create(viewModelType);
            NavigateToInternal(viewModel, message.Parameter);
        }
        else
        {
            _logger?.LogWarning("未找到页面路由: {PageName}", message.PageName);
        }
    }

    public void NavigateTo<TViewModel>(object? parameter = null) where TViewModel : ViewModelBase
    {
        var viewModel = _pageViewModelFactory.Create<TViewModel>();
        NavigateToInternal(viewModel, parameter);
    }

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

        if (viewModel is INavigationAware navigationAware)
        {
            navigationAware.OnNavigatedTo(parameter);
        }

        // 3. 更新状态（触发UI变更）
        UpdateState();
    }

    public bool GoBack()
    {
        if (!CanGoBack)
        {
            _logger?.LogWarning("无法返回，导航栈为空或只有一个页面");
            return false;
        }

        var poppedItem = _navigationStack.Pop();

        if (poppedItem.ViewModel is INavigationAware poppedAware)
        {
            poppedAware.OnNavigatedFrom();
        }

        DisposeViewModel(poppedItem.ViewModel);

        if (_navigationStack.Count > 0)
        {
            var currentItem = _navigationStack.Peek();

            _logger?.LogInformation("返回到页面: {PageType}", currentItem.ViewModel.GetType().Name);

            // 2. 通知重新显示的页面（Re-activation）
            // 传递 isReactivation: true，让页面区分首次进入与 GoBack 重新激活，
            // 避免重复执行订阅、加载等副作用
            if (currentItem.ViewModel is INavigationAware currentAware)
            {
                currentAware.OnNavigatedTo(currentItem.Parameter, isReactivation: true);
            }

            // 3. 更新状态（触发UI变更）
            UpdateState();
        }
        else
        {
            UpdateState();
        }

        return true;
    }

    /// <summary>
    /// 清空导航栈并导航到指定页面（用于根级别导航）
    /// </summary>
    public void NavigateToRoot(ViewModelBase viewModel, string rootNavigationItemTitle)
    {
        // 清空并释放所有页面：先通知 OnNavigatedFrom 再 Dispose，确保资源正确清理
        while (_navigationStack.Count > 0)
        {
            var item = _navigationStack.Pop();
            if (item.ViewModel is INavigationAware aware)
                aware.OnNavigatedFrom();
            DisposeViewModel(item.ViewModel);
        }

        NavigateToInternal(viewModel, null, rootNavigationItemTitle);
    }

    public int GetStackDepth() => _navigationStack.Count;

    public void Clear()
    {
        while (_navigationStack.Count > 0)
        {
            var item = _navigationStack.Pop();
            if (item.ViewModel is INavigationAware aware)
                aware.OnNavigatedFrom();
            DisposeViewModel(item.ViewModel);
        }

        UpdateState();
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

    private void DisposeViewModel(ViewModelBase? viewModel)
    {
        if (viewModel is not IDisposable disposable)
            return;

        try
        {
            disposable.Dispose();
            _logger?.LogDebug("已释放 ViewModel 资源: {Type}", viewModel.GetType().Name);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "释放 ViewModel 资源时发生错误: {Type}", viewModel.GetType().Name);
        }
    }
}

