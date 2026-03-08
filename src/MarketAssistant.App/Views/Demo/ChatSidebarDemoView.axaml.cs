using Avalonia.Controls;
using MarketAssistant.ViewModels.Demo;

namespace MarketAssistant.Views.Demo;

public partial class ChatSidebarDemoView : UserControl
{
    public ChatSidebarDemoView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }
}
