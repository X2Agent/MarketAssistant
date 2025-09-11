using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Agents;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
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
    public ICommand ShowAnalysisDetailsCommand { get; private set; }

    public AgentAnalysisViewModel(
        MarketAnalysisAgent marketAnalysisAgent,
        AnalysisReportViewModel analysisReportViewModel,
        ILogger<AgentAnalysisViewModel> logger) : base(logger)
    {
        _marketAnalysisAgent = marketAnalysisAgent;
        _analysisReportViewModel = analysisReportViewModel;

        SubscribeToEvents();
        ToggleViewCommand = new RelayCommand(ToggleView);
        ShowAnalysisDetailsCommand = new RelayCommand(ShowAnalysisDetails);
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
            AnalysisReportViewModel.ProcessAnalysisMessage(message);
            AnalysisReportViewModel.IsReportVisible = true;
        });
    }

    private async void ShowAnalysisDetails()
    {
        await Shell.Current.DisplayAlert("功能提示", "查看分析详情功能正在开发中，敬请期待！", "确定");
    }

    public async Task LoadAnalysisDataAsync()
    {
        if (string.IsNullOrEmpty(StockCode))
            return;

        await SafeExecuteAsync(async () =>
        {
            AnalysisMessages.Clear();
            AnalysisReportViewModel.ProcessAnalysisMessage(new AnalysisMessage());
            var history = await _marketAnalysisAgent.AnalysisAsync(StockCode);
            foreach (var message in history)
            {
                if (message.Role != AuthorRole.Assistant)
                {
                    continue; // 只处理助手的消息
                }
                if (string.IsNullOrEmpty(message.Content.Replace("\n\n", "")))
                {
                    continue;
                }
                AnalysisMessages.Add(new AnalysisMessage()
                {
                    Sender = message.AuthorName ?? string.Empty,
                    Content = message.Content ?? string.Empty,
                    Timestamp = DateTime.Now,
                    InputTokenCount = 0,
                    OutputTokenCount = 0,
                    TotalTokenCount = 0
                });
            }
        }, "股票分析");
    }
}