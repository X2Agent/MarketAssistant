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
            System.Diagnostics.Debug.WriteLine($"CardView: HeaderProperty changed, new value = {e.NewValue?.GetType().Name ?? "null"}");
            x.UpdateHeader();
        });
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        System.Diagnostics.Debug.WriteLine($"CardView.OnApplyTemplate called");

        // 获取模板元素 (使用 PART_ 命名约定)
        _stringHeaderLabel = e.NameScope.Find<TextBlock>("PART_StringHeaderLabel");
        _viewHeaderPresenter = e.NameScope.Find<ContentPresenter>("PART_ViewHeaderContentPresenter");
        _divider = e.NameScope.Find<Border>("PART_Divider");

        System.Diagnostics.Debug.WriteLine($"CardView: Found parts - StringLabel: {_stringHeaderLabel != null}, ViewPresenter: {_viewHeaderPresenter != null}, Divider: {_divider != null}");
        System.Diagnostics.Debug.WriteLine($"CardView: Current Header value = {Header?.GetType().Name ?? "null"}");

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
            System.Diagnostics.Debug.WriteLine($"CardView.UpdateHeader: Parts not ready");
            return;
        }

        try
        {
            var header = Header;
            System.Diagnostics.Debug.WriteLine($"CardView.UpdateHeader: Header type = {header?.GetType().Name ?? "null"}");

            if (header is string headerText && !string.IsNullOrEmpty(headerText))
            {
                // 显示文本标题
                System.Diagnostics.Debug.WriteLine($"CardView.UpdateHeader: String header = {headerText}");
                _stringHeaderLabel.Text = headerText;
                _stringHeaderLabel.IsVisible = true;
                _viewHeaderPresenter.IsVisible = false;
                _viewHeaderPresenter.Content = null;
                _divider.IsVisible = true;
            }
            else if (header != null)
            {
                // 显示视图标题（任何非null对象）
                System.Diagnostics.Debug.WriteLine($"CardView.UpdateHeader: Object header");
                _viewHeaderPresenter.Content = header;
                _viewHeaderPresenter.IsVisible = true;
                _stringHeaderLabel.IsVisible = false;
                _divider.IsVisible = true;
            }
            else
            {
                // 隐藏所有标题元素
                System.Diagnostics.Debug.WriteLine($"CardView.UpdateHeader: No header");
                _stringHeaderLabel.IsVisible = false;
                _viewHeaderPresenter.IsVisible = false;
                _divider.IsVisible = false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CardView.UpdateHeader error: {ex.Message}");
        }
    }
}
