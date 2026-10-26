using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.ViewModels;

namespace MarketAssistant.Views.Controls;

/// <summary>
/// 统一的加载 / 空态 / 失败浮层。各页面共享同一套呈现，避免每个页面各写一份。
/// </summary>
public partial class StateOverlayView : UserControl
{
    private const string DefaultEmptyTitle = "暂无数据";
    private const string DefaultFailedTitle = "数据加载失败";
    private const string DefaultLoadingTitle = "正在加载...";

    private bool _isLoadingState;
    private bool _isEmptyState;
    private bool _isFailedState;
    private bool _hasMessage;
    private bool _canRetry;
    private string _titleText = string.Empty;

    public static readonly StyledProperty<LoadState> StateProperty =
        AvaloniaProperty.Register<StateOverlayView, LoadState>(nameof(State));

    /// <summary>提示正文。留空时不显示正文行。</summary>
    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<StateOverlayView, string?>(nameof(Message));

    /// <summary>提示标题。留空时按状态给出默认文案。</summary>
    public static readonly StyledProperty<string?> TitleOverrideProperty =
        AvaloniaProperty.Register<StateOverlayView, string?>(nameof(TitleOverride));

    public static readonly StyledProperty<IAsyncRelayCommand?> RetryCommandProperty =
        AvaloniaProperty.Register<StateOverlayView, IAsyncRelayCommand?>(nameof(RetryCommand));

    public static readonly StyledProperty<string> RetryTextProperty =
        AvaloniaProperty.Register<StateOverlayView, string>(nameof(RetryText), "重试");

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<StateOverlayView, double>(nameof(IconSize), 48.0);

    /// <summary>
    /// 是否由浮层呈现加载态。页面自带骨架屏时置为 false，避免骨架与加载提示叠加。
    /// </summary>
    public static readonly StyledProperty<bool> ShowLoadingProperty =
        AvaloniaProperty.Register<StateOverlayView, bool>(nameof(ShowLoading), true);

    private static readonly DirectProperty<StateOverlayView, bool> IsLoadingStateProperty =
        AvaloniaProperty.RegisterDirect<StateOverlayView, bool>(
            nameof(IsLoadingState), o => o._isLoadingState);

    private static readonly DirectProperty<StateOverlayView, bool> IsEmptyStateProperty =
        AvaloniaProperty.RegisterDirect<StateOverlayView, bool>(
            nameof(IsEmptyState), o => o._isEmptyState);

    private static readonly DirectProperty<StateOverlayView, bool> IsFailedStateProperty =
        AvaloniaProperty.RegisterDirect<StateOverlayView, bool>(
            nameof(IsFailedState), o => o._isFailedState);

    private static readonly DirectProperty<StateOverlayView, bool> HasMessageProperty =
        AvaloniaProperty.RegisterDirect<StateOverlayView, bool>(
            nameof(HasMessage), o => o._hasMessage);

    private static readonly DirectProperty<StateOverlayView, bool> CanRetryProperty =
        AvaloniaProperty.RegisterDirect<StateOverlayView, bool>(
            nameof(CanRetry), o => o._canRetry);

    private static readonly DirectProperty<StateOverlayView, string> TitleTextProperty =
        AvaloniaProperty.RegisterDirect<StateOverlayView, string>(
            nameof(TitleText), o => o._titleText);

    public LoadState State
    {
        get => GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string? TitleOverride
    {
        get => GetValue(TitleOverrideProperty);
        set => SetValue(TitleOverrideProperty, value);
    }

    public IAsyncRelayCommand? RetryCommand
    {
        get => GetValue(RetryCommandProperty);
        set => SetValue(RetryCommandProperty, value);
    }

    public string RetryText
    {
        get => GetValue(RetryTextProperty);
        set => SetValue(RetryTextProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public bool ShowLoading
    {
        get => GetValue(ShowLoadingProperty);
        set => SetValue(ShowLoadingProperty, value);
    }

    public bool IsLoadingState => _isLoadingState;

    public bool IsEmptyState => _isEmptyState;

    public bool IsFailedState => _isFailedState;

    public bool HasMessage => _hasMessage;

    /// <summary>仅失败态提供重试入口；空态重试无意义。</summary>
    public bool CanRetry => _canRetry;

    public string TitleText => _titleText;

    static StateOverlayView()
    {
        StateProperty.Changed.AddClassHandler<StateOverlayView>((view, _) => view.RefreshDerivedState());
        MessageProperty.Changed.AddClassHandler<StateOverlayView>((view, _) => view.RefreshDerivedState());
        TitleOverrideProperty.Changed.AddClassHandler<StateOverlayView>((view, _) => view.RefreshDerivedState());
        RetryCommandProperty.Changed.AddClassHandler<StateOverlayView>((view, _) => view.RefreshDerivedState());
        ShowLoadingProperty.Changed.AddClassHandler<StateOverlayView>((view, _) => view.RefreshDerivedState());
    }

    public StateOverlayView()
    {
        InitializeComponent();
        RefreshDerivedState();
    }

    private void RefreshDerivedState()
    {
        var showLoading = State == LoadState.Loading && ShowLoading;

        IsVisible = showLoading || State is LoadState.Empty or LoadState.Failed;

        var title = TitleOverride ?? State switch
        {
            LoadState.Empty => DefaultEmptyTitle,
            LoadState.Failed => DefaultFailedTitle,
            LoadState.Loading => DefaultLoadingTitle,
            _ => string.Empty
        };

        SetAndRaise(IsLoadingStateProperty, ref _isLoadingState, showLoading);
        SetAndRaise(IsEmptyStateProperty, ref _isEmptyState, State == LoadState.Empty);
        SetAndRaise(IsFailedStateProperty, ref _isFailedState, State == LoadState.Failed);
        SetAndRaise(HasMessageProperty, ref _hasMessage, !string.IsNullOrWhiteSpace(Message));
        SetAndRaise(CanRetryProperty, ref _canRetry, State == LoadState.Failed && RetryCommand != null);
        SetAndRaise(TitleTextProperty, ref _titleText, title);
    }
}
