using Avalonia.Controls;
using Avalonia.Controls.Templates;
using MarketAssistant.Avalonia.ViewModels;
using MarketAssistant.Avalonia.Views;
using System;

namespace MarketAssistant.Infrastructure.Core
{
    /// <summary>
    /// 视图定位器，用于根据ViewModel类型创建对应的View
    /// </summary>
    public class ViewLocator : IDataTemplate
    {
        public Control? Build(object? data)
        {
            if (data is null)
                return null;

            var viewModelType = data.GetType();
            var viewTypeName = viewModelType.FullName!.Replace("ViewModel", "View");
            var viewType = Type.GetType(viewTypeName);

            if (viewType != null)
            {
                var control = (Control?)Activator.CreateInstance(viewType);
                if (control != null)
                {
                    control.DataContext = data;
                    return control;
                }
            }

            // 如果找不到对应的View，使用具体的映射
            return data switch
            {
                HomePageViewModel => new HomePageView { DataContext = data },
                FavoritesPageViewModel => new FavoritesPageView { DataContext = data },
                StockSelectionPageViewModel => new StockSelectionPageView { DataContext = data },
                SettingsPageViewModel => new SettingsPageView { DataContext = data },
                AboutPageViewModel => new AboutPageView { DataContext = data },
                MCPConfigPageViewModel => new MCPConfigPageView { DataContext = data },
                StockPageViewModel => new StockPageView { DataContext = data },
                AgentAnalysisViewModel => new AgentAnalysisPageView { DataContext = data },
                _ => new TextBlock { Text = $"未找到视图: {viewModelType.Name}" }
            };
        }

        public bool Match(object? data)
        {
            return data is ViewModelBase;
        }
    }
}

