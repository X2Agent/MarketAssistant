using Avalonia.Controls;
using Avalonia.Controls.Templates;
using MarketAssistant.ViewModels;
using MarketAssistant.ViewModels.Trading;
using MarketAssistant.Views.Pages;
using MarketAssistant.Views.Pages.Trading;

namespace MarketAssistant.Infrastructure.Core;

/// <summary>
/// 视图定位器，用于根据ViewModel类型创建对应的View
/// </summary>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null)
            return null;

        return data switch
        {
            HomePageViewModel => new HomePageView { DataContext = data },
            FavoritesPageViewModel => new FavoritesPageView { DataContext = data },
            PriceAlertPageViewModel => new PriceAlertPageView { DataContext = data },
            AssetSelectionPageViewModel => new AssetSelectionPageView { DataContext = data },
            SettingsPageViewModel => new SettingsPageView { DataContext = data },
            AboutPageViewModel => new AboutPageView { DataContext = data },
            MCPConfigPageViewModel => new MCPConfigPageView { DataContext = data },
            AssetPageViewModel => new AssetPageView { DataContext = data },
            AgentAnalysisViewModel => new AgentAnalysisPageView { DataContext = data },
            TradingPageViewModel => new TradingPageView { DataContext = data },
            BalanceDetailPageViewModel => new BalanceDetailPageView { DataContext = data },
            _ => new TextBlock { Text = $"未找到视图: {data.GetType().Name}" }
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}

