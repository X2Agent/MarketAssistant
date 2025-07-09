using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Agents;
using MarketAssistant.Services;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels;

/// <summary>
/// AI选股功能的ViewModel
/// </summary>
public class StockSelectionViewModel : ViewModelBase
{
    private readonly StockSelectionService _stockSelectionService;

    private string _userRequirements = string.Empty;
    /// <summary>
    /// 用户选股需求
    /// </summary>
    public string UserRequirements
    {
        get => _userRequirements;
        set => SetProperty(ref _userRequirements, value);
    }

    private string _selectionResult = string.Empty;
    /// <summary>
    /// 选股结果
    /// </summary>
    public string SelectionResult
    {
        get => _selectionResult;
        set => SetProperty(ref _selectionResult, value);
    }

    private bool _hasResult;
    /// <summary>
    /// 是否有选股结果
    /// </summary>
    public bool HasResult
    {
        get => _hasResult;
        set => SetProperty(ref _hasResult, value);
    }

    private ObservableCollection<QuickSelectionStrategyInfo> _quickStrategies = new();
    /// <summary>
    /// 快速选股策略列表
    /// </summary>
    public ObservableCollection<QuickSelectionStrategyInfo> QuickStrategies
    {
        get => _quickStrategies;
        set => SetProperty(ref _quickStrategies, value);
    }

    /// <summary>
    /// 执行AI选股命令
    /// </summary>
    public IRelayCommand ExecuteSelectionCommand { get; private set; }

    /// <summary>
    /// 执行快速选股命令
    /// </summary>
    public IRelayCommand<QuickSelectionStrategyInfo> ExecuteQuickSelectionCommand { get; private set; }

    /// <summary>
    /// 清除结果命令
    /// </summary>
    public IRelayCommand ClearResultCommand { get; private set; }

    public StockSelectionViewModel(
        ILogger<StockSelectionViewModel> logger,
        StockSelectionService stockSelectionService) : base(logger)
    {
        _stockSelectionService = stockSelectionService;

        ExecuteSelectionCommand = new RelayCommand(async () => await ExecuteSelectionAsync());
        ExecuteQuickSelectionCommand = new RelayCommand<QuickSelectionStrategyInfo>(async (strategy) => await ExecuteQuickSelectionAsync(strategy));
        ClearResultCommand = new RelayCommand(ClearResult);

        _ = LoadQuickStrategiesAsync();
    }

    /// <summary>
    /// 执行AI选股
    /// </summary>
    private async Task ExecuteSelectionAsync()
    {
        if (string.IsNullOrWhiteSpace(UserRequirements))
        {
            await Application.Current.MainPage.DisplayAlert("提示", "请输入选股需求", "确定");
            return;
        }

        await SafeExecuteAsync(async () =>
        {
            var result = await _stockSelectionService.SelectStocksAsync(UserRequirements);
            SelectionResult = result;
            HasResult = !string.IsNullOrEmpty(result);
        }, "执行AI选股");
    }

    /// <summary>
    /// 执行快速选股
    /// </summary>
    private async Task ExecuteQuickSelectionAsync(QuickSelectionStrategyInfo? strategy)
    {
        if (strategy == null)
            return;

        await SafeExecuteAsync(async () =>
        {
            var result = await _stockSelectionService.QuickSelectAsync(strategy.Strategy);
            SelectionResult = result;
            HasResult = !string.IsNullOrEmpty(result);
            
            // 更新用户需求显示为所选策略
            UserRequirements = $"快速选股：{strategy.Name}";
        }, $"执行快速选股：{strategy.Name}");
    }

    /// <summary>
    /// 清除选股结果
    /// </summary>
    private void ClearResult()
    {
        SelectionResult = string.Empty;
        HasResult = false;
        UserRequirements = string.Empty;
    }

    /// <summary>
    /// 加载快速选股策略
    /// </summary>
    private async Task LoadQuickStrategiesAsync()
    {
        await SafeExecuteAsync(() =>
        {
            var strategies = _stockSelectionService.GetQuickSelectionStrategies();
            QuickStrategies.Clear();
            
            foreach (var strategy in strategies)
            {
                QuickStrategies.Add(strategy);
            }
            return Task.CompletedTask;
        }, "加载快速选股策略");
    }
}