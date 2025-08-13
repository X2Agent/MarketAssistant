using MarketAssistant.ViewModels;

namespace MarketAssistant.Pages
{
    public partial class DataSourceSelectionPage : ContentPage
    {
        public DataSourceSelectionPage(DataSourceSelectionViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
        }
    }
}