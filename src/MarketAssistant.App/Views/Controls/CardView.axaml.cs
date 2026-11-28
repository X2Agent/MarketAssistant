using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;

namespace MarketAssistant.Views.Controls;

/// <summary>
/// 卡片视图控件，支持标题区（字符串或任意视图）。
/// v3 起卡片不再绘制分隔线，标题与内容之间由标题的底部外边距建立间距。
/// </summary>
public partial class CardView : ContentControl
{
    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<CardView, object?>(nameof(Header), null);

    /// <summary>
    /// 卡片标题，可以是字符串或视图
    /// </summary>
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    private TextBlock? _stringHeaderLabel;
    private ContentPresenter? _viewHeaderPresenter;

    static CardView()
    {
        HeaderProperty.Changed.AddClassHandler<CardView>((x, e) =>
        {
            x.UpdateHeader();
        });
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        // 获取模板元素 (使用 PART_ 命名约定)
        _stringHeaderLabel = e.NameScope.Find<TextBlock>("PART_StringHeaderLabel");
        _viewHeaderPresenter = e.NameScope.Find<ContentPresenter>("PART_ViewHeaderContentPresenter");

        // 初始化状态
        if (_stringHeaderLabel != null) _stringHeaderLabel.IsVisible = false;
        if (_viewHeaderPresenter != null)
        {
            _viewHeaderPresenter.IsVisible = false;
            _viewHeaderPresenter.Content = null;
        }

        // 应用当前Header值
        UpdateHeader();
    }

    /// <summary>
    /// 更新标题显示
    /// </summary>
    private void UpdateHeader()
    {
        if (_stringHeaderLabel == null || _viewHeaderPresenter == null)
        {
            return;
        }

        var header = Header;

        if (header is string headerText && !string.IsNullOrEmpty(headerText))
        {
            // 显示文本标题
            _stringHeaderLabel.Text = headerText;
            _stringHeaderLabel.IsVisible = true;
            _viewHeaderPresenter.IsVisible = false;
            _viewHeaderPresenter.Content = null;
        }
        else if (header != null)
        {
            // 显示视图标题（任何非null对象）
            _viewHeaderPresenter.Content = header;
            _viewHeaderPresenter.IsVisible = true;
            _stringHeaderLabel.IsVisible = false;
        }
        else
        {
            // 隐藏所有标题元素
            _stringHeaderLabel.IsVisible = false;
            _viewHeaderPresenter.IsVisible = false;
        }
    }
}
