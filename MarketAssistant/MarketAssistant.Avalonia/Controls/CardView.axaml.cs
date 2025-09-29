using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;

namespace MarketAssistant.Avalonia.Controls;

/// <summary>
/// 卡片视图控件，支持标题和阴影效果
/// </summary>
public partial class CardView : TemplatedControl
{
    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<CardView, object?>(nameof(Header), null);

    public static readonly StyledProperty<double> ShadowOpacityProperty =
        AvaloniaProperty.Register<CardView, double>(nameof(ShadowOpacity), 0.6);

    public static readonly StyledProperty<object?> ContentProperty =
        ContentControl.ContentProperty.AddOwner<CardView>();

    /// <summary>
    /// 卡片标题，可以是字符串或视图
    /// </summary>
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// 阴影透明度
    /// </summary>
    public double ShadowOpacity
    {
        get => GetValue(ShadowOpacityProperty);
        set => SetValue(ShadowOpacityProperty, value);
    }

    /// <summary>
    /// 卡片内容
    /// </summary>
    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    private TextBlock? _stringHeaderLabel;
    private ContentPresenter? _viewHeaderPresenter;
    private Border? _divider;

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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        
        if (change.Property == HeaderProperty)
        {
            UpdateHeader();
        }
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

        try
        {
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
            else if (header is Control headerView)
            {
                // 显示视图标题
                _viewHeaderPresenter.Content = headerView;
                _stringHeaderLabel.IsVisible = false;
                _viewHeaderPresenter.IsVisible = true;
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
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CardView.UpdateHeader error: {ex.Message}");
        }
    }
}
