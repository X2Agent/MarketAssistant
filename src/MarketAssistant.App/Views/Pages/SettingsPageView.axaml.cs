using Avalonia.Controls;
using Avalonia.Input;
using MarketAssistant.Infrastructure.Providers;
using MarketAssistant.ViewModels;

namespace MarketAssistant.Views.Pages;

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

    /// <summary>
    /// 服务商框获得用户焦点（点击/Tab）时展开全量候选列表。
    /// AutoCompleteBox 不会像 ComboBox 那样点击弹出，且 MinimumPrefixLength=0 时空文本即全量展示。
    /// 排除 Unspecified：选择提交后控件会程序化归还焦点，此时不能重开下拉。
    /// </summary>
    private void OnProviderBoxGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.NavigationMethod != NavigationMethod.Unspecified
            && sender is AutoCompleteBox box
            && !box.IsDropDownOpen)
        {
            box.IsDropDownOpen = true;
        }
    }

    /// <summary>
    /// 服务商下拉关闭时恢复显示当前选中项，避免未提交的搜索残留与实际选中项不一致。
    /// 仅影响文本框显示，不回写 ViewModel。
    /// </summary>
    private void OnProviderBoxDropDownClosed(object? sender, EventArgs e)
    {
        if (sender is AutoCompleteBox box && box.SelectedItem is ModelProvider provider)
        {
            box.Text = provider.DisplayName;
        }
    }

    /// <summary>
    /// 模型框获得用户焦点（点击/Tab）时展开候选列表，配合输入即时过滤。
    /// </summary>
    private void OnModelBoxGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.NavigationMethod != NavigationMethod.Unspecified
            && sender is AutoCompleteBox box
            && !box.IsDropDownOpen)
        {
            box.IsDropDownOpen = true;
        }
    }
}