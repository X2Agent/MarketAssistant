using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MarketAssistant.Views.Components;

/// <summary>
/// 分析报告视图
/// </summary>
public partial class AnalysisReportView : UserControl
{
    public AnalysisReportView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

