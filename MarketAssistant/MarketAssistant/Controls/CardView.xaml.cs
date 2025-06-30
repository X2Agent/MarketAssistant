namespace MarketAssistant.Controls;

[ContentProperty("Content")]
public partial class CardView : ContentView
{
    public static readonly BindableProperty HeaderProperty =
        BindableProperty.Create(nameof(Header), typeof(object), typeof(CardView), null,
            propertyChanged: OnHeaderChanged);

    public object Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public static readonly BindableProperty ShadowOpacityProperty =
        BindableProperty.Create(nameof(ShadowOpacity), typeof(float), typeof(CardView), 0.6f);

    public float ShadowOpacity
    {
        get => (float)GetValue(ShadowOpacityProperty);
        set => SetValue(ShadowOpacityProperty, value);
    }

    private Label _stringHeaderLabel;
    private ContentPresenter _viewHeaderPresenter;
    private BoxView _divider;

    public CardView()
    {
        InitializeComponent();
        
        // 监听BindingContext变化，确保Header内容能获得正确的绑定上下文
        this.BindingContextChanged += CardView_BindingContextChanged;
    }
    
    private void CardView_BindingContextChanged(object sender, EventArgs e)
    {
        // 当BindingContext变化时，重新应用Header，确保绑定正确传递
        if (Header is View headerView && BindingContext != null)
        {
            headerView.BindingContext = BindingContext;
        }
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // 通过模板查找元素
        _stringHeaderLabel = (Label)GetTemplateChild("StringHeaderLabel");
        _viewHeaderPresenter = (ContentPresenter)GetTemplateChild("ViewHeaderContentPresenter");
        _divider = (BoxView)GetTemplateChild("Divider");

        // 确保元素初始状态正确
        if (_stringHeaderLabel != null) _stringHeaderLabel.IsVisible = false;
        if (_viewHeaderPresenter != null) 
        {
            _viewHeaderPresenter.IsVisible = false;
            _viewHeaderPresenter.Content = null;
        }
        if (_divider != null) _divider.IsVisible = false;
        
        // 应用当前Header值
        OnHeaderChanged(this, null, Header);
    }

    private static void OnHeaderChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var cardView = (CardView)bindable;

        if (cardView is null)
        {
            return;
        }

        try
        {
            // 延迟处理，确保在模板加载后执行
            if (cardView._stringHeaderLabel == null ||
                cardView._viewHeaderPresenter == null ||
                cardView._divider == null)
            {
                // 如果元素尚未准备好，可能模板还未加载
                // 等待 OnCardViewLoaded 处理
                return;
            }

            // 检查新值类型，根据类型显示不同的头部
            if (newValue is string headerText && !string.IsNullOrEmpty(headerText))
            {
                cardView._stringHeaderLabel.Text = headerText;
                cardView._stringHeaderLabel.IsVisible = true;
                cardView._viewHeaderPresenter.IsVisible = false;
                cardView._viewHeaderPresenter.Content = null;
                cardView._divider.IsVisible = true;
            }
            else if (newValue is View headerView)
            {
                // 确保视图的绑定上下文正确传递
                if (headerView.BindingContext == null && cardView.BindingContext != null)
                {
                    headerView.BindingContext = cardView.BindingContext;
                }
                
                // 先清除旧内容，避免"Element is already the child of another element"错误
                cardView._viewHeaderPresenter.Content = null;
                cardView._viewHeaderPresenter.Content = headerView;
                cardView._stringHeaderLabel.IsVisible = false;
                cardView._viewHeaderPresenter.IsVisible = true;
                cardView._divider.IsVisible = true;
            }
            else
            {
                cardView._stringHeaderLabel.IsVisible = false;
                cardView._viewHeaderPresenter.IsVisible = false;
                cardView._divider.IsVisible = false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CardView.OnHeaderChanged error: {ex.Message}");
        }
    }
}