using MarketAssistant.ViewModels;

namespace MarketAssistant.Pages;

public partial class AgentAnalysisPage : ContentPage
{
    public AgentAnalysisPage(AgentAnalysisViewModel viewModel)
    {
        InitializeComponent();

        BindingContext = viewModel;

        // 页面加载完成后加载数据
        Loaded += OnPageLoaded;
    }

    private void OnPageLoaded(object sender, EventArgs e)
    {
        // 在后台线程执行耗时操作
        _ = Task.Run(async () =>
        {
            await (BindingContext as AgentAnalysisViewModel)!.LoadAnalysisDataAsync();
        });
    }
}