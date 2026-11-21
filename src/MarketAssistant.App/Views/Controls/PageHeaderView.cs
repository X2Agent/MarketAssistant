using Avalonia;
using Avalonia.Controls;

namespace MarketAssistant.Views.Controls;

/// <summary>
/// 页面头：标题（24px）+ 行内说明（12px）+ 右侧操作槽（Content）。
/// 全库唯一使用点是自选页，操作槽承载「卡片视图 / 表格视图」切换；
/// 其它页面的标题由左侧导航（根页面）或返回窄条（二级页面）表达。
/// </summary>
public class PageHeaderView : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<PageHeaderView, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<PageHeaderView, string?>(nameof(Description));

    /// <summary>页面标题（如"自选看板"）。</summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>行内说明，与标题同行垂直居中；窄窗下按字符截断，不再换行增高。</summary>
    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}
