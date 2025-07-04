using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Agents;
using MarketAssistant.Services;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace MarketAssistant.ViewModels;

[QueryProperty(nameof(StockCode), "code")]
public partial class AgentAnalysisViewModel : ViewModelBase
{
    private readonly MarketAnalysisAgent _marketAnalysisAgent;

    private string _stockCode = "";
    public string StockCode
    {
        get => _stockCode;
        set
        {
            _stockCode = value;
            OnPropertyChanged();
        }
    }

    private string _currentAnalyst = "准备中";
    public string CurrentAnalyst
    {
        get => _currentAnalyst;
        set => SetProperty(ref _currentAnalyst, value);
    }

    private bool _isAnalysisInProgress;
    public bool IsAnalysisInProgress
    {
        get => _isAnalysisInProgress;
        set => SetProperty(ref _isAnalysisInProgress, value);
    }

    private string _analysisStage = "等待开始分析";
    public string AnalysisStage
    {
        get => _analysisStage;
        set => SetProperty(ref _analysisStage, value);
    }

    public ObservableCollection<AnalysisMessage> AnalysisMessages { get; } = new ObservableCollection<AnalysisMessage>();

    private AnalysisMessage? _finalAnalysisMessage;
    public AnalysisMessage? FinalAnalysisMessage
    {
        get => _finalAnalysisMessage;
        set => SetProperty(ref _finalAnalysisMessage, value);
    }

    private bool _isRawDataViewVisible;
    public bool IsRawDataViewVisible
    {
        get => _isRawDataViewVisible;
        set => SetProperty(ref _isRawDataViewVisible, value);
    }

    private AnalysisReportViewModel _analysisReportViewModel;
    public AnalysisReportViewModel AnalysisReportViewModel
    {
        get => _analysisReportViewModel;
        set => SetProperty(ref _analysisReportViewModel, value);
    }

    public ICommand ToggleViewCommand { get; private set; }
    public ICommand ViewKLineChartCommand { get; private set; }

    private readonly IWindowsService _windowsService;

    public AgentAnalysisViewModel(
        MarketAnalysisAgent marketAnalysisAgent,
        IWindowsService windowsService,
        AnalysisReportViewModel analysisReportViewModel,
        ILogger<AgentAnalysisViewModel> logger) : base(logger)
    {
        _marketAnalysisAgent = marketAnalysisAgent;
        _windowsService = windowsService;
        _analysisReportViewModel = analysisReportViewModel;

        SubscribeToEvents();
        ToggleViewCommand = new RelayCommand(ToggleView);
        ViewKLineChartCommand = new RelayCommand(ViewKLineChart);
    }

    private void SubscribeToEvents()
    {
        _marketAnalysisAgent.ProgressChanged += OnAnalysisProgressChanged;
        _marketAnalysisAgent.AnalysisCompleted += OnAnalysisCompleted;
    }

    private void ToggleView()
    {
        IsRawDataViewVisible = !IsRawDataViewVisible;
    }

    private async void ViewKLineChart()
    {
        if (string.IsNullOrEmpty(StockCode))
            return;

        await SafeExecuteAsync(async () =>
        {
            // 首先尝试激活已存在的StockPage窗口
            if (_windowsService.ActivateWindowByPageType(typeof(Pages.StockPage)))
            {
                // 如果成功激活了已存在的窗口，直接返回
                return;
            }

            // 如果没有已存在的窗口，创建新窗口
            var stockViewModel = Application.Current.Handler.MauiContext.Services.GetService<StockViewModel>();
            if (stockViewModel != null)
            {
                stockViewModel.StockCode = StockCode;
                var stockPage = new Pages.StockPage(stockViewModel)
                {
                    Title = $"K线图 - {StockCode}"
                };

                // 优先返回主窗口
                var parentWindow = Shell.Current.GetParentWindow();
                if (parentWindow == null)
                {
                    parentWindow = Application.Current?.Windows?.FirstOrDefault();
                }

                var window = await _windowsService.ShowWindowAsync(stockPage, parentWindow);
            }
        }, "打开K线图窗口");
    }

    private void OnAnalysisProgressChanged(object sender, AnalysisProgressEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            CurrentAnalyst = e.CurrentAnalyst;
            IsAnalysisInProgress = e.IsInProgress;
            AnalysisStage = e.StageDescription;
        });
    }

    private void OnAnalysisCompleted(object sender, ChatMessageContent e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var message = new AnalysisMessage
            {
                Sender = e.AuthorName ?? string.Empty,
                Content = e.Content ?? string.Empty,
                Timestamp = DateTime.Now,
                InputTokenCount = 0,
                OutputTokenCount = 0,
                TotalTokenCount = 0
            };

            AnalysisMessages.Add(message);
            FinalAnalysisMessage = message;
            AnalysisReportViewModel.IsReportVisible = true;
        });
    }

    public async Task LoadAnalysisDataAsync()
    {
        if (string.IsNullOrEmpty(StockCode))
            return;

        await SafeExecuteAsync(async () =>
        {
            AnalysisMessages.Clear();
            FinalAnalysisMessage = null;
            await _marketAnalysisAgent.AnalyzeStockAsync(StockCode);
        }, "股票分析");
    }
}