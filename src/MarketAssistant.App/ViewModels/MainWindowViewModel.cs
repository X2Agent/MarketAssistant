using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Services;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Navigation;
using MarketAssistant.Services.Notification;
using MarketAssistant.ViewModels.Trading;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MarketAssistant.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    // 页面 ViewModel 由工厂在导航项点击时才实例化，避免构造主窗口时实例化全部页面
    private readonly IPageViewModelFactory _pageViewModelFactory;
    private readonly NavigationService _navigationService;
    private readonly MarketContext _marketContext;
    private readonly INotificationService _notificationService;
    private bool _isSynchronizingNavigationSelection;

    // 主导航与底部导航必须各自持有选中项：两个 ListBox 绑定同一属性时，
    // 任一选中变化会让另一个列表把 SelectedIndex 归 -1 并回写 null，导致侧栏高亮丢失
    [ObservableProperty]
    private NavigationItemViewModel? _selectedMainNavigationItem;

    [ObservableProperty]
    private NavigationItemViewModel? _selectedBottomNavigationItem;

    public ViewModelBase? CurrentPage => _navigationService.CurrentPage;
    public bool CanGoBack => _navigationService.CanGoBack;
    public string CurrentPageTitle => _navigationService.CurrentPage?.Title ?? string.Empty;

    public ObservableCollection<NavigationItemViewModel> MainNavigationItems { get; }

    public ObservableCollection<NavigationItemViewModel> BottomNavigationItems { get; }

    /// <summary>
    /// 顶栏行情条（当前为模拟数据，待接入真实指数服务）
    /// </summary>
    public ObservableCollection<IndexTickerItemViewModel> IndexTickers { get; }

    /// <summary>
    /// 行情条是否可见（无数据时整段隐藏，对齐设计系统裁决 #6）
    /// </summary>
    public bool HasIndexTickers => IndexTickers.Count > 0;

    /// <summary>
    /// 当前市场类型显示文本
    /// </summary>
    public string CurrentMarketText => _marketContext.CurrentMarket == MarketType.AShare ? "A股市场" : "虚拟币市场";

    /// <summary>
    /// 当前是否为 A 股市场（用于顶栏分段切换器视觉状态）
    /// </summary>
    public bool IsAShareMarket => _marketContext.CurrentMarket == MarketType.AShare;

    /// <summary>
    /// 当前是否为虚拟币市场（用于顶栏分段切换器视觉状态）
    /// </summary>
    public bool IsCryptoMarket => _marketContext.CurrentMarket == MarketType.Crypto;

    public MainWindowViewModel(
        IPageViewModelFactory pageViewModelFactory,
        NavigationService navigationService,
        MarketContext marketContext,
        INotificationService notificationService,
        ILogger<MainWindowViewModel>? logger = null)
        : base(logger)
    {
        _pageViewModelFactory = pageViewModelFactory;
        _navigationService = navigationService;
        _marketContext = marketContext;
        _notificationService = notificationService;

        MainNavigationItems = new ObservableCollection<NavigationItemViewModel>();
        BottomNavigationItems = new ObservableCollection<NavigationItemViewModel>();
        IndexTickers = new ObservableCollection<IndexTickerItemViewModel>();
        RebuildNavigationItems();
        RebuildIndexTickers();

        _navigationService.PropertyChanged += OnNavigationServicePropertyChanged;

        SubscribeToMarketChanges(_marketContext);

        // 默认导航到首页。选中项的变更回调负责实际导航，避免重复入栈。
        SelectedMainNavigationItem = MainNavigationItems[0];
    }

    protected override void OnMarketChanged(MarketType newMarket)
    {
        OnPropertyChanged(nameof(CurrentMarketText));
        OnPropertyChanged(nameof(IsAShareMarket));
        OnPropertyChanged(nameof(IsCryptoMarket));
        RebuildNavigationItems();
        RebuildIndexTickers();
    }

    private void RebuildNavigationItems()
    {
        MainNavigationItems.Clear();
        BottomNavigationItems.Clear();

        MainNavigationItems.Add(new NavigationItemViewModel("首页", "avares://MarketAssistant/Assets/Images/tab_home.svg", "avares://MarketAssistant/Assets/Images/tab_home_on.svg", () => _pageViewModelFactory.Create<HomePageViewModel>()));
        MainNavigationItems.Add(new NavigationItemViewModel("收藏", "avares://MarketAssistant/Assets/Images/tab_favorites.svg", "avares://MarketAssistant/Assets/Images/tab_favorites_on.svg", () => _pageViewModelFactory.Create<FavoritesPageViewModel>()));
        MainNavigationItems.Add(new NavigationItemViewModel("告警", "avares://MarketAssistant/Assets/Images/tab_alert.svg", "avares://MarketAssistant/Assets/Images/tab_alert_on.svg", () => _pageViewModelFactory.Create<PriceAlertPageViewModel>()));
        MainNavigationItems.Add(new NavigationItemViewModel("AI选股", "avares://MarketAssistant/Assets/Images/tab_analysis.svg", "avares://MarketAssistant/Assets/Images/tab_analysis_on.svg", () => _pageViewModelFactory.Create<AssetSelectionPageViewModel>()));
        // 交易入口跟随市场能力：虚拟币等支持交易的市场可见，A 股不可见
        if (IsTradingVisible())
        {
            MainNavigationItems.Add(new NavigationItemViewModel("交易", "avares://MarketAssistant/Assets/Images/tab_trading.svg", "avares://MarketAssistant/Assets/Images/tab_trading_on.svg", () => _pageViewModelFactory.Create<TradingPageViewModel>()));
        }

        // 底部固定：设置 / 关于（对齐原型 sidebar nav-bot）
        BottomNavigationItems.Add(new NavigationItemViewModel("设置", "avares://MarketAssistant/Assets/Images/tab_settings.svg", "avares://MarketAssistant/Assets/Images/tab_settings_on.svg", () => _pageViewModelFactory.Create<SettingsPageViewModel>()));
        BottomNavigationItems.Add(new NavigationItemViewModel("关于", "avares://MarketAssistant/Assets/Images/tab_about.svg", "avares://MarketAssistant/Assets/Images/tab_about_on.svg", () => _pageViewModelFactory.Create<AboutPageViewModel>()));
    }

    /// <summary>
    /// 按当前市场填充顶栏行情条。
    /// 接入真实指数服务前保持为空（顶栏随 HasIndexTickers 自动隐藏）：
    /// 在 App.Services 对应市场模块（AShareMarketModule / CryptoMarketModule）注册
    /// IIndexQuoteService（Keyed by MarketType），此处改为调用其接口填充即可，XAML 无需改动。
    /// </summary>
    private void RebuildIndexTickers()
    {
        IndexTickers.Clear();

        OnPropertyChanged(nameof(HasIndexTickers));
    }

    /// <summary>
    /// 交易导航可见性：仅当前市场支持交易时可见（如虚拟币）；A 股始终不可见。
    /// </summary>
    private bool IsTradingVisible()
        => _marketContext.CurrentCapability.SupportsTrading;

    private void OnNavigationServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NavigationService.CurrentPage))
        {
            OnPropertyChanged(nameof(CurrentPage));
            OnPropertyChanged(nameof(CurrentPageTitle));
        }
        else if (e.PropertyName == nameof(NavigationService.CanGoBack))
        {
            OnPropertyChanged(nameof(CanGoBack));
        }
        else if (e.PropertyName == nameof(NavigationService.CurrentRootNavigationItemTitle))
        {
            if (_navigationService.CurrentRootNavigationItemTitle != null)
            {
                _isSynchronizingNavigationSelection = true;
                try
                {
                    var mainItem = MainNavigationItems.FirstOrDefault(
                        item => item.Title == _navigationService.CurrentRootNavigationItemTitle);
                    SelectedMainNavigationItem = mainItem;
                    SelectedBottomNavigationItem = mainItem == null
                        ? BottomNavigationItems.FirstOrDefault(
                            item => item.Title == _navigationService.CurrentRootNavigationItemTitle)
                        : null;
                }
                finally
                {
                    _isSynchronizingNavigationSelection = false;
                }
            }
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        _navigationService.GoBack();
    }

    /// <summary>
    /// 切换市场命令（Logo点击或Ctrl+M快捷键）
    /// </summary>
    [RelayCommand]
    private void ToggleMarket()
    {
        var newMarket = _marketContext.CurrentMarket == MarketType.AShare
            ? MarketType.Crypto
            : MarketType.AShare;

        SwitchToMarket(newMarket);
    }

    /// <summary>
    /// 按指定市场切换（顶栏分段切换器使用）
    /// </summary>
    [RelayCommand]
    private void SwitchMarket(MarketType market)
    {
        if (_marketContext.CurrentMarket == market)
            return;

        SwitchToMarket(market);
    }

    /// <summary>
    /// 执行市场切换、提示并刷新当前页面
    /// </summary>
    private void SwitchToMarket(MarketType newMarket)
    {
        // 切市场会触发导航集合重建（Clear 使 ListBox 清空选中并回写 null），
        // 必须先把当前页标题缓存到局部变量，切完按标题重新定位并导航
        var currentTitle = SelectedMainNavigationItem?.Title ?? SelectedBottomNavigationItem?.Title;

        _marketContext.SwitchMarket(newMarket);

        var marketName = newMarket == MarketType.AShare ? "A股市场" : "虚拟币市场";
        _notificationService.ShowSuccess($"已切换到{marketName}");

        Logger?.LogInformation("市场已切换到: {Market} ({MarketName})", newMarket, marketName);

        // 按标题重新定位导航项：原市场特有的页面（如交易）在新市场不存在时回退到首页
        var target = currentTitle != null
            ? MainNavigationItems.FirstOrDefault(item => item.Title == currentTitle)
              ?? BottomNavigationItems.FirstOrDefault(item => item.Title == currentTitle)
            : null;
        target ??= MainNavigationItems[0];

        var viewModel = target.CreateViewModel();
        _navigationService.NavigateToRoot(viewModel, target.Title);

        // 同步两个列表的选中态（NavigateToRoot 会经 NavigationService 事件同步，此处兜底显式设置）
        _isSynchronizingNavigationSelection = true;
        try
        {
            SelectedMainNavigationItem = MainNavigationItems.Contains(target) ? target : null;
            SelectedBottomNavigationItem = BottomNavigationItems.Contains(target) ? target : null;
        }
        finally
        {
            _isSynchronizingNavigationSelection = false;
        }
    }

    partial void OnSelectedMainNavigationItemChanged(NavigationItemViewModel? value)
        => OnNavigationItemSelected(value);

    partial void OnSelectedBottomNavigationItemChanged(NavigationItemViewModel? value)
        => OnNavigationItemSelected(value);

    private void OnNavigationItemSelected(NavigationItemViewModel? value)
    {
        if (value is null || _isSynchronizingNavigationSelection)
            return;

        // 避免重复导航
        if (_navigationService.CurrentRootNavigationItemTitle == value.Title)
            return;

        var viewModel = value.CreateViewModel();
        _navigationService.NavigateToRoot(viewModel, value.Title);
    }
}

public class NavigationItemViewModel : ViewModelBase
{
    public override string Title { get; }
    public string IconPath { get; }
    public string SelectedIconPath { get; }
    public Func<ViewModelBase> CreateViewModel { get; }

    public NavigationItemViewModel(string title, string iconPath, string selectedIconPath, Func<ViewModelBase> createViewModel)
    {
        Title = title;
        IconPath = iconPath;
        SelectedIconPath = selectedIconPath;
        CreateViewModel = createViewModel;
    }
}
