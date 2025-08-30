using MarketAssistant.Applications.Stocks;
using MarketAssistant.Applications.Stocks.Models;
using MarketAssistant.Applications.Telegrams;
using MarketAssistant.Infrastructure;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace MarketAssistant.ViewModels;

public class HomeViewModel : ViewModelBase, IDisposable
{
    public ICommand SearchCommand { get; set; }
    public ICommand SelectStockCommand { get; set; }
    public ICommand SelectHotStockCommand { get; set; }
    public ICommand SelectRecentStockCommand { get; set; }
    public ICommand AddToFavoriteCommand { get; set; }
    public ICommand OpenNewsCommand { get; set; }
    public ObservableCollection<StockItem> StockResults { get; set; } = new ObservableCollection<StockItem>();
    public ObservableCollection<HotStock> HotStocks { get; set; } = new ObservableCollection<HotStock>();
    public ObservableCollection<StockItem> RecentStocks { get; set; } = new ObservableCollection<StockItem>();
    public ObservableCollection<Telegram> Telegraphs { get; set; } = new ObservableCollection<Telegram>();

    // 统一的定时器，用于更新新闻快讯和倒计时
    private System.Timers.Timer _updateTimer;

    private string _telegraphRefreshCountdown = "";
    public string TelegraphRefreshCountdown
    {
        get => _telegraphRefreshCountdown;
        set => SetProperty(ref _telegraphRefreshCountdown, value);
    }

    private bool _isSearchResultVisible;
    public bool IsSearchResultVisible
    {
        get => _isSearchResultVisible;
        set => SetProperty(ref _isSearchResultVisible, value);
    }

    private readonly StockService _stockService;
    private readonly StockSearchHistory _searchHistory;
    private readonly StockFavoriteService _favoriteService;
    private readonly TelegramService _telegramService;

    public HomeViewModel(
        ILogger<HomeViewModel> logger,
        StockService stockService,
        StockFavoriteService favoriteService,
        StockSearchHistory searchHistory,
        TelegramService telegramService) : base(logger)
    {
        _stockService = stockService;
        _searchHistory = searchHistory;
        _favoriteService = favoriteService;
        _telegramService = telegramService;

        // 记录应用程序启动日志
        SearchCommand = new Command<string>(async (query) => await OnSearch(query));
        SelectStockCommand = new Command<StockItem>(OnSelectStock);
        SelectHotStockCommand = new Command<HotStock>(OnSelectHotStock);
        SelectRecentStockCommand = new Command<StockItem>(OnSelectRecentStock);
        AddToFavoriteCommand = new Command<object>(OnAddToFavorite);
        OpenNewsCommand = new Command<Telegram>(OnOpenTelegram);

        InitializeHotStocks();
        InitializeRecentStocks();

        // 设置统一的定时器，每秒触发一次
        _updateTimer = new System.Timers.Timer(1000); // 1秒
        _updateTimer.Elapsed += async (sender, e) =>
        {
            await GlobalExceptionHandler.SafeExecuteAsync(async () =>
            {
                // 确保UI更新在主线程上执行
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    UpdateCountdown();
                });

                // 每10秒更新一次新闻
                if (DateTime.Now.Second % 10 == 0)
                {
                    await UpdateNewsItemsAsync();
                }
            }, operationName: "定时器更新", logger: Logger);
        };
        _updateTimer.AutoReset = true;

        _ = UpdateNewsItemsAsync();
    }

    private async Task OnSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            IsSearchResultVisible = false;
            return;
        }

        await SafeExecuteAsync(async () =>
        {
            var results = await _stockService.SearchStockAsync(query, CancellationToken.None);
            StockResults.Clear();

            foreach (var stock in results)
            {
                StockResults.Add(new StockItem { Name = stock.Name, Code = stock.Code });
            }
        }, "搜索股票");

        IsSearchResultVisible = true;
    }

    private async void OnSelectStock(StockItem stock)
    {
        await SafeExecuteAsync(async () =>
        {
            // 导航到股票详情页
            IsSearchResultVisible = false;

            // 添加到最近查看历史记录
            _searchHistory.AddSearchHistory(stock);
            // 刷新最近查看列表
            InitializeRecentStocks();

            await Shell.Current.GoToAsync("analysis", new Dictionary<string, object>
            {
                { "code", stock.Code }
            });
        }, "导航到股票详情页");
    }

    private async void OnSelectHotStock(HotStock stock)
    {
        await SafeExecuteAsync(async () =>
        {
            var stockCode = $"{stock.Market}{stock.Code}".ToLower();
            // 添加到最近查看历史记录
            _searchHistory.AddSearchHistory(new StockItem { Name = stock.Name, Code = stockCode });
            // 刷新最近查看列表
            InitializeRecentStocks();

            await Shell.Current.GoToAsync("analysis", new Dictionary<string, object>
            {
                { "code", stockCode }
            });
        }, "OnSelectHotStock");
    }

    private async void OnSelectRecentStock(StockItem stock)
    {
        await SafeExecuteAsync(async () =>
        {
            await Shell.Current.GoToAsync("analysis", new Dictionary<string, object>
            {
                { "code", stock.Code }
            });
        }, "OnSelectRecentStock");
    }

    private async void InitializeHotStocks()
    {
        try
        {
            // 获取真实热门股票数据
            HotStocks.Clear();
            var hotStocks = await _stockService.GetHotStocksAsync();

            // 转换为UI显示模型
            foreach (var stock in hotStocks)
            {
                HotStocks.Add(stock);
            }
        }
        catch (Exception ex)
        {
            // 处理异常，加载失败时显示默认数据
            Logger?.LogError("加载热门股票数据失败: {Message}", ex.Message);
        }
    }

    private void InitializeRecentStocks()
    {
        // 从本地存储加载最近查看的股票记录
        var recentStocks = _searchHistory.GetSearchHistory();
        RecentStocks.Clear();

        foreach (var stock in recentStocks)
        {
            RecentStocks.Add(stock);
        }
    }

    private void UpdateCountdown()
    {
        try
        {
            var seconds = DateTime.Now.Second % 10;
            var nextUpdate = (seconds == 0) ? 10 : (10 - seconds);
            TelegraphRefreshCountdown = $"{nextUpdate}秒后更新";
        }
        catch (Exception ex)
        {
            Logger?.LogError("更新倒计时出错: {Message}", ex.Message);
            TelegraphRefreshCountdown = "更新中...";
        }
    }

    private async Task UpdateNewsItemsAsync()
    {
        try
        {
            // 更新状态为正在获取数据
            MainThread.BeginInvokeOnMainThread(() => TelegraphRefreshCountdown = "正在更新...");

            var news = await _telegramService.GetTelegraphsAsync(CancellationToken.None);

            // 确保在主线程上更新UI集合
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Telegraphs.Clear();
                foreach (var item in news)
                {
                    Telegraphs.Add(item);
                }
            });

            return;
        }
        catch (Exception ex)
        {
            Logger?.LogError("获取咨询时出错: {Message}", ex.Message);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                TelegraphRefreshCountdown = "更新失败";
            });
        }
    }

    private async void OnOpenTelegram(Telegram telegram)
    {
        // 打开新闻详情，可以使用浏览器或内置WebView
        if (!string.IsNullOrEmpty(telegram.Url))
        {
            try
            {
                await Browser.Default.OpenAsync(telegram.Url, BrowserLaunchMode.SystemPreferred);
            }
            catch (Exception ex)
            {
                Logger?.LogError("打开新闻链接时出错: {Message}", ex.Message);
                await Shell.Current.DisplayAlert("错误", "无法打开新闻链接", "确定");
            }
        }
    }

    /// <summary>
    /// 添加股票到收藏列表
    /// </summary>
    private void OnAddToFavorite(object parameter)
    {
        try
        {
            if (parameter is HotStock hotStock)
            {
                // 直接添加热门股票到收藏
                _favoriteService.AddFavorite(hotStock.Code, hotStock.Market);
                // 显示添加成功的提示
                Shell.Current.DisplayAlert("收藏成功", $"已将 {hotStock.Name} 添加到收藏列表", "确定");
            }
            else if (parameter is StockItem stockItem)
            {
                // 将StockItem转换为HotStock格式
                string market = "";
                string code = stockItem.Code;

                // 尝试从股票代码中提取市场代码
                if (code.StartsWith("sh") || code.StartsWith("sz"))
                {
                    market = code.Substring(0, 2).ToUpper();
                    code = code.Substring(2);
                }

                _favoriteService.AddFavorite(code, market);
                // 显示添加成功的提示
                Shell.Current.DisplayAlert("收藏成功", $"已将 {stockItem.Name} 添加到收藏列表", "确定");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError("添加收藏时出错: {Message}", ex.Message);
            Shell.Current.DisplayAlert("收藏失败", "添加收藏时发生错误，请稍后重试", "确定");
        }
    }

    /// <summary>
    /// 启动定时器
    /// </summary>
    public void StartTimer()
    {
        if (_updateTimer != null && !_updateTimer.Enabled)
        {
            _updateTimer.Start();
            Logger?.LogInformation("定时器已启动");
        }
    }

    /// <summary>
    /// 暂停定时器
    /// </summary>
    public void StopTimer()
    {
        if (_updateTimer != null && _updateTimer.Enabled)
        {
            _updateTimer.Stop();
            Logger?.LogInformation("定时器已暂停");
        }
    }

    /// <summary>
    /// 释放资源，停止并销毁定时器
    /// </summary>
    public void Dispose()
    {
        if (_updateTimer != null)
        {
            _updateTimer.Stop();
            // 但停止定时器并释放资源可以防止事件继续触发
            _updateTimer.Dispose();
            _updateTimer = null!; // 使用 null-forgiving 运算符，明确告知编译器此处赋值为 null 是安全的
        }
    }
}