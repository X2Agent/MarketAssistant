using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.AlertCenter;
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
    private readonly IAlertCenterService _alertCenterService;
    private bool _isSynchronizingNavigationSelection;

    /// <summary>告警中心未读数（导航徽标）。AlertsChanged 时刷新。</summary>
    [ObservableProperty]
    private int _unreadAlertCount;

    /// <summary>是否存在未读告警（控制导航图标徽标可见性）。</summary>
    public bool HasUnreadAlerts => UnreadAlertCount > 0;

    partial void OnUnreadAlertCountChanged(int value)
        => OnPropertyChanged(nameof(HasUnreadAlerts));

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
    /// 顶栏行情条（市场级聚合指标：A 股三大指数 / Crypto 总市值·主导率·恐惧贪婪）
    /// </summary>
    public ObservableCollection<IndexTickerItemViewModel> IndexTickers { get; }

    /// <summary>行情条定时刷新器（30s），构造时创建并启动。</summary>
    private Avalonia.Threading.DispatcherTimer? _tickerTimer;

    /// <summary>
    /// 行情条刷新请求代数：每次发起刷新自增，仅最新一代结果落地。
    /// 替代 bool 防重入——切市场时若旧请求在飞，新请求不会被丢弃，旧结果落地前被淘汰，
    /// 消除"切市场后行情条残留上一个市场数据"的竞态。
    /// </summary>
    private int _tickerRequestId;

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
        IAlertCenterService alertCenterService,
        ILogger<MainWindowViewModel>? logger = null)
        : base(logger)
    {
        _pageViewModelFactory = pageViewModelFactory;
        _navigationService = navigationService;
        _marketContext = marketContext;
        _notificationService = notificationService;
        _alertCenterService = alertCenterService;

        MainNavigationItems = new ObservableCollection<NavigationItemViewModel>();
        BottomNavigationItems = new ObservableCollection<NavigationItemViewModel>();
        IndexTickers = new ObservableCollection<IndexTickerItemViewModel>();
        RebuildNavigationItems();
        _ = RefreshIndexTickersAsync();
        StartTickerTimer();

        _navigationService.PropertyChanged += OnNavigationServicePropertyChanged;

        // 订阅告警中心：新告警/合并/已读时刷新导航未读徽标
        _alertCenterService.AlertsChanged += OnAlertsChanged;
        _ = RefreshUnreadAlertCountAsync();

        SubscribeToMarketChanges(_marketContext);

        // 默认导航到首页。选中项的变更回调负责实际导航，避免重复入栈。
        SelectedMainNavigationItem = MainNavigationItems[0];
    }

    /// <summary>告警集合变化回调：切回 UI 线程刷新未读数。</summary>
    private void OnAlertsChanged()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = RefreshUnreadAlertCountAsync());
    }

    /// <summary>从告警中心拉取未读数并同步导航徽标，失败静默（不影响主流程）。</summary>
    private async Task RefreshUnreadAlertCountAsync()
    {
        try
        {
            UnreadAlertCount = await _alertCenterService.GetUnreadCountAsync();
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "刷新告警未读数失败");
        }

        // 切市场会重建导航集合（徽标状态被重置），每次刷新都重新定位告警项回填
        var alertItem = MainNavigationItems.FirstOrDefault(item => item.Title == "告警");
        if (alertItem != null)
            alertItem.HasUnreadBadge = UnreadAlertCount > 0;
    }

    protected override void OnMarketChanged(MarketType newMarket)
    {
        OnPropertyChanged(nameof(CurrentMarketText));
        OnPropertyChanged(nameof(IsAShareMarket));
        OnPropertyChanged(nameof(IsCryptoMarket));
        RebuildNavigationItems();
        _ = RefreshIndexTickersAsync();

        // 导航集合重建后徽标状态被重置，按当前未读数回填
        _ = RefreshUnreadAlertCountAsync();
    }

    private void RebuildNavigationItems()
    {
        MainNavigationItems.Clear();
        BottomNavigationItems.Clear();

        MainNavigationItems.Add(new NavigationItemViewModel("首页", "avares://MarketAssistant/Assets/Images/tab_home.svg", "avares://MarketAssistant/Assets/Images/tab_home_on.svg", () => _pageViewModelFactory.Create<HomePageViewModel>()));
        MainNavigationItems.Add(new NavigationItemViewModel("自选", "avares://MarketAssistant/Assets/Images/tab_favorites.svg", "avares://MarketAssistant/Assets/Images/tab_favorites_on.svg", () => _pageViewModelFactory.Create<FavoritesPageViewModel>()));
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
    /// 按当前市场拉取市场级聚合指标填充顶栏行情条。
    /// 通过 Keyed 解析 <see cref="IIndexQuoteService"/>（A 股=三大指数，Crypto=总市值/主导率/恐惧贪婪）；
    /// 无数据或失败时保持为空，顶栏随 <see cref="HasIndexTickers"/> 自动隐藏。
    /// 并发策略：允许并发刷新，落地前校验请求代数，仅最新一代结果生效。
    /// </summary>
    private async Task RefreshIndexTickersAsync()
    {
        // 发起即自增代数：切市场/定时刷新并发时，只有最新一代的结果落地
        var requestId = Interlocked.Increment(ref _tickerRequestId);

        try
        {
            var market = _marketContext.CurrentMarket;
            var items = await _marketContext.GetService<IIndexQuoteService>(market).GetLatestAsync();

            // 拉取期间已发起过更新的刷新或已切市场，丢弃过期结果
            if (requestId != Volatile.Read(ref _tickerRequestId) || market != _marketContext.CurrentMarket)
                return;

            IndexTickers.Clear();
            foreach (var item in items)
            {
                IndexTickers.Add(IndexTickerItemViewModel.FromTrend(item.Name, item.Value, item.ChangeText, item.Trend));
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "刷新顶栏行情条失败");
        }
        finally
        {
            OnPropertyChanged(nameof(HasIndexTickers));
        }
    }

    /// <summary>启动行情条 30s 定时刷新（惰性创建于 UI 线程）。</summary>
    private void StartTickerTimer()
    {
        _tickerTimer ??= new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30),
        };
        _tickerTimer.Tick -= OnTickerTimerTick;
        _tickerTimer.Tick += OnTickerTimerTick;
        _tickerTimer.Start();
    }

    private void OnTickerTimerTick(object? sender, EventArgs e) => _ = RefreshIndexTickersAsync();

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

    /// <summary>是否显示未读徽标（仅告警导航项在存在未读告警时为 true）。</summary>
    private bool _hasUnreadBadge;
    public bool HasUnreadBadge
    {
        get => _hasUnreadBadge;
        set => SetProperty(ref _hasUnreadBadge, value);
    }

    public NavigationItemViewModel(string title, string iconPath, string selectedIconPath, Func<ViewModelBase> createViewModel)
    {
        Title = title;
        IconPath = iconPath;
        SelectedIconPath = selectedIconPath;
        CreateViewModel = createViewModel;
    }
}
