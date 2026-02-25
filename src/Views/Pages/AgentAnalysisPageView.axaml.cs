using Avalonia.Controls;
using MarketAssistant.ViewModels;

namespace MarketAssistant.Views.Pages;

/// <summary>
/// 代理分析页面视图
/// </summary>
public partial class AgentAnalysisPageView : UserControl
{
    public AgentAnalysisPageView()
    {
        InitializeComponent();

        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is AgentAnalysisViewModel vm)
            {
                var topLevel = TopLevel.GetTopLevel(this);
                vm.SetStorageProvider(topLevel?.StorageProvider);
            }
        };
    }
}
