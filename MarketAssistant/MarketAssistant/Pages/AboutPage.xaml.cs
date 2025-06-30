using MarketAssistant.ViewModels;

namespace MarketAssistant.Pages;

public partial class AboutPage : ContentPage
{

    public AboutPage(AboutViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}