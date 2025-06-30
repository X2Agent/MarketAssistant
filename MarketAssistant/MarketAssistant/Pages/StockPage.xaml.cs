using MarketAssistant.ViewModels;

namespace MarketAssistant.Pages;

public partial class StockPage : ContentPage
{
    private readonly StockViewModel _viewModel;

    public StockPage(StockViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;

        // 订阅ViewModel属性变化事件
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // 确保首次进入页面时加载数据
        if (_viewModel.KLineData == null || !_viewModel.KLineData.Any())
        {
            _viewModel.RefreshDataCommand.Execute(null);
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // 取消订阅事件，防止内存泄漏
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }

    private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 当K线数据更新时，更新图表
        if (e.PropertyName == nameof(StockViewModel.KLineData) ||
            e.PropertyName == nameof(StockViewModel.CurrentKLineType))
        {
            UpdateCharts();
        }
    }

    private void UpdateCharts()
    {
        if (_viewModel.KLineData == null || !_viewModel.KLineData.Any())
            return;

        try
        {
            // 更新WebView图表
            WebChartView.SetTitleAsync(_viewModel.StockName).ConfigureAwait(false);
            WebChartView.UpdateChartAsync(_viewModel.KLineData).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"更新图表时发生错误: {ex.Message}");
        }
    }
}