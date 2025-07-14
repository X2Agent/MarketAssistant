using MarketAssistant.ViewModels;

namespace MarketAssistant.Pages;

/// <summary>
/// AI选股页面
/// </summary>
public partial class StockSelectionPage : ContentPage
{
    public StockSelectionPage(StockSelectionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // 页面出现时聚焦到需求输入框
        RequirementsEditor.Focus();
    }
}