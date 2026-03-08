using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;

namespace MarketAssistant.Views.Controls;

/// <summary>
/// 卡片视图控件，支持标题和阴影效果
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
    private Border? _divider;

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
        _divider = e.NameScope.Find<Border>("PART_Divider");

        // 初始化状态
        if (_stringHeaderLabel != null) _stringHeaderLabel.IsVisible = false;
        if (_viewHeaderPresenter != null) 
        {
            _viewHeaderPresenter.IsVisible = false;
            _viewHeaderPresenter.Content = null;
        }
        if (_divider != null) _divider.IsVisible = false;
        
        // 应用当前Header值
        UpdateHeader();
    }


    /// <summary>
    /// 更新标题显示
    /// </summary>
    private void UpdateHeader()
    {
        if (_stringHeaderLabel == null || _viewHeaderPresenter == null || _divider == null)
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
            _divider.IsVisible = true;
        }
        else if (header != null)
        {
            // 显示视图标题（任何非null对象）
            _viewHeaderPresenter.Content = header;
            _viewHeaderPresenter.IsVisible = true;
            _stringHeaderLabel.IsVisible = false;
            _divider.IsVisible = true;
        }
        else
        {
            // 隐藏所有标题元素
            _stringHeaderLabel.IsVisible = false;
            _viewHeaderPresenter.IsVisible = false;
            _divider.IsVisible = false;
        }
    }
}
