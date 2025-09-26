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

        // 确保UI操作在主线程执行
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                // 更新WebView图表
                await WebChartView.SetTitleAsync(_viewModel.StockCode);
                await WebChartView.UpdateChartAsync(_viewModel.KLineData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新图表时发生错误: {ex.Message}");
            }
        });
    }
}