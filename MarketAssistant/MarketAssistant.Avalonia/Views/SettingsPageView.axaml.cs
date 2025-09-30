using Avalonia.Controls;
using MarketAssistant.Avalonia.ViewModels;

namespace MarketAssistant.Avalonia.Views;

public partial class SettingsPageView : UserControl
{
    public SettingsPageView()
    {
        InitializeComponent();
        
        // 当控件附加到可视树时设置 StorageProvider
        AttachedToVisualTree += (s, e) =>
        {
            if (DataContext is SettingsPageViewModel viewModel)
            {
                var topLevel = TopLevel.GetTopLevel(this);
                viewModel.SetStorageProvider(topLevel?.StorageProvider);
            }
        };
    }
}