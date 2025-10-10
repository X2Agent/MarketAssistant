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
public class NavigationService
{
    private readonly ILogger<NavigationService>? _logger;
    private readonly Stack<NavigationItem> _navigationStack = new();

    public event EventHandler<NavigationItem>? Navigated;
    public event EventHandler? CanGoBackChanged;

    public bool CanGoBack => _navigationStack.Count > 1;

    public ViewModelBase? CurrentPage => _navigationStack.Count > 0 ? _navigationStack.Peek().ViewModel : null;

    public string? CurrentRootNavigationItemTitle => _navigationStack.Count > 0 ? _navigationStack.Peek().RootNavigationItemTitle : null;

    public NavigationService(ILogger<NavigationService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 导航到新页面（用于子页面导航，保留当前根导航项）
    /// </summary>
    public void NavigateTo(ViewModelBase viewModel, object? parameter = null)
    {
        // 如果已经有导航历史，使用当前的根导航项
        var rootNavigationItemTitle = CurrentRootNavigationItemTitle;
        var navigationItem = new NavigationItem(viewModel, rootNavigationItemTitle, parameter);
        _navigationStack.Push(navigationItem);

        _logger?.LogInformation("导航到页面: {PageType}, 根导航项: {RootItem}",
            viewModel.GetType().Name, rootNavigationItemTitle ?? "无");

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
            Navigated?.Invoke(this, currentItem);
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

        NavigateTo(viewModel, rootNavigationItemTitle);
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

        CanGoBackChanged?.Invoke(this, EventArgs.Empty);
    }
}

