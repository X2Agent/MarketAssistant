using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Navigation;
using MarketAssistant.Services.Notification;
using MarketAssistant.Services.Settings;
using MarketAssistant.ViewModels.Trading;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MarketAssistant.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly NavigationService _navigationService;
    private readonly MarketContext _marketContext;
    private readonly INotificationService _notificationService;
    private readonly IUserSettingService _userSettingService;
    private bool _isSynchronizingNavigationSelection;

    [ObservableProperty]
    private NavigationItemViewModel? _selectedNavigationItem;

    public ViewModelBase? CurrentPage => _navigationService.CurrentPage;
    public bool CanGoBack => _navigationService.CanGoBack;
    public string CurrentPageTitle => _navigationService.CurrentPage?.Title ?? string.Empty;

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }

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
        IServiceProvider serviceProvider,
        NavigationService navigationService,
        MarketContext marketContext,
        INotificationService notificationService,
        IUserSettingService userSettingService,
        ILogger<MainWindowViewModel>? logger = null)
        : base(logger)
    {
        _serviceProvider = serviceProvider;
        _navigationService = navigationService;
        _marketContext = marketContext;
        _notificationService = notificationService;
        _userSettingService = userSettingService;

        NavigationItems = new ObservableCollection<NavigationItemViewModel>();
        RebuildNavigationItems();

        // 监听导航服务属性变更
        _navigationService.PropertyChanged += OnNavigationServicePropertyChanged;

        // 监听市场切换事件
        SubscribeToMarketChanges(_marketContext);

        // 默认导航到首页。SelectedNavigationItem 的变更回调负责实际导航，避免重复入栈。
        SelectedNavigationItem = NavigationItems[0];
    }

    protected override void OnMarketChanged(MarketType newMarket)
    {
        OnPropertyChanged(nameof(CurrentMarketText));
        OnPropertyChanged(nameof(IsAShareMarket));
        OnPropertyChanged(nameof(IsCryptoMarket));
        RebuildNavigationItems();
    }

    private void RebuildNavigationItems()
    {
        NavigationItems.Clear();

        NavigationItems.Add(new NavigationItemViewModel("首页", "avares://MarketAssistant/Assets/Images/tab_home.svg", "avares://MarketAssistant/Assets/Images/tab_home_on.svg", () => _serviceProvider.GetRequiredService<HomePageViewModel>()));
        NavigationItems.Add(new NavigationItemViewModel("收藏", "avares://MarketAssistant/Assets/Images/tab_favorites.svg", "avares://MarketAssistant/Assets/Images/tab_favorites_on.svg", () => _serviceProvider.GetRequiredService<FavoritesPageViewModel>()));
        NavigationItems.Add(new NavigationItemViewModel("告警", "avares://MarketAssistant/Assets/Images/tab_alert.svg", "avares://MarketAssistant/Assets/Images/tab_alert_on.svg", () => _serviceProvider.GetRequiredService<PriceAlertPageViewModel>()));
        NavigationItems.Add(new NavigationItemViewModel("AI选股", "avares://MarketAssistant/Assets/Images/tab_analysis.svg", "avares://MarketAssistant/Assets/Images/tab_analysis_on.svg", () => _serviceProvider.GetRequiredService<AssetSelectionPageViewModel>()));
        // 交易为实验功能：仅当前市场支持交易且用户在设置中显式开启时可见（默认关闭）
        if (IsTradingVisible())
        {
            NavigationItems.Add(new NavigationItemViewModel("交易", "avares://MarketAssistant/Assets/Images/tab_trading.svg", "avares://MarketAssistant/Assets/Images/tab_trading_on.svg", () => _serviceProvider.GetRequiredService<TradingPageViewModel>()));
        }
        NavigationItems.Add(new NavigationItemViewModel("设置", "avares://MarketAssistant/Assets/Images/tab_settings.svg", "avares://MarketAssistant/Assets/Images/tab_settings_on.svg", () => _serviceProvider.GetRequiredService<SettingsPageViewModel>()));
        NavigationItems.Add(new NavigationItemViewModel("关于", "avares://MarketAssistant/Assets/Images/tab_about.svg", "avares://MarketAssistant/Assets/Images/tab_about_on.svg", () => _serviceProvider.GetRequiredService<AboutPageViewModel>()));
    }

    /// <summary>
    /// 交易导航可见性：市场支持交易（如虚拟币）且用户显式开启实验开关；A 股始终不可见。
    /// </summary>
    private bool IsTradingVisible()
        => _marketContext.CurrentCapability.SupportsTrading
           && _userSettingService.CurrentSetting.EnableExperimentalTrading;

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
                    SelectedNavigationItem = NavigationItems.FirstOrDefault(
                        item => item.Title == _navigationService.CurrentRootNavigationItemTitle);
                }
                finally
                {
                    _isSynchronizingNavigationSelection = false;
                }
            }
        }
    }

    /// <summary>
    /// 返回命令
    /// </summary>
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
        // 切换市场类型
        var newMarket = _marketContext.CurrentMarket == MarketType.AShare
            ? MarketType.Crypto
            : MarketType.AShare;

        SwitchToMarket(newMarket);
    }

    /// <summary>
    /// 按指定市场切换（顶栏分段切换器使用）
    /// </summary>
    /// <param name="market">目标市场类型</param>
    [RelayCommand]
    private void SwitchMarket(string market)
    {
        if (!Enum.TryParse<MarketType>(market, out var targetMarket))
            return;

        // 已是目标市场则不重复切换刷新页面
        if (_marketContext.CurrentMarket == targetMarket)
            return;

        SwitchToMarket(targetMarket);
    }

    /// <summary>
    /// 执行市场切换、提示并刷新当前页面
    /// </summary>
    private void SwitchToMarket(MarketType newMarket)
    {
        _marketContext.SwitchMarket(newMarket);

        // 显示切换提示
        var marketName = newMarket == MarketType.AShare ? "A股市场" : "虚拟币市场";
        _notificationService.ShowSuccess($"已切换到{marketName}");

        Logger?.LogInformation("市场已切换到: {Market} ({MarketName})", newMarket, marketName);

        // 刷新当前页面（重新加载数据）
        if (SelectedNavigationItem != null)
        {
            var currentTitle = SelectedNavigationItem.Title;
            var viewModel = SelectedNavigationItem.CreateViewModel();
            _navigationService.NavigateToRoot(viewModel, currentTitle);
        }
    }

    partial void OnSelectedNavigationItemChanged(NavigationItemViewModel? value)
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
