using Avalonia.Controls;
using Avalonia.Threading;
using MarketAssistant.Avalonia.ViewModels;
using MarketAssistant.Avalonia.Views.Components;
using System.ComponentModel;

namespace MarketAssistant.Avalonia.Views;

/// <summary>
/// 股票详情页视图
/// </summary>
public partial class StockPageView : UserControl
{
    private StockPageViewModel? _viewModel;

    public StockPageView()
    {
        InitializeComponent();
        
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // 取消订阅旧的 ViewModel
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        // 订阅新的 ViewModel
        _viewModel = DataContext as StockPageViewModel;
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 当K线数据更新时，更新图表
        if (e.PropertyName == nameof(StockPageViewModel.KLineData) ||
            e.PropertyName == nameof(StockPageViewModel.CurrentKLineType))
        {
            UpdateCharts();
        }
    }

    private void UpdateCharts()
    {
        if (_viewModel?.KLineData == null || !_viewModel.KLineData.Any())
            return;

        // 确保UI操作在主线程执行
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                // 更新WebView图表
                if (WebChartView != null)
                {
                    await WebChartView.UpdateChartAsync(_viewModel.KLineData);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"更新图表时发生错误: {ex.Message}");
            }
        });
    }
}
