using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Transformation;
using MarketAssistant.ViewModels;

namespace MarketAssistant.Views.Pages;

/// <summary>
/// 资产详情页视图
/// </summary>
public partial class AssetPageView : UserControl
{
    private static readonly TransformOperations ScaledUp = TransformOperations.Parse("scale(1.18)");
    private static readonly TransformOperations Identity = TransformOperations.Parse("scale(1)");

    private bool _isIconPopping;

    public AssetPageView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 收藏按钮：先等命令落库，再只对"加进自选"这一侧播放反馈。
    /// 取消收藏是一件安静的事，弹烟花会读成"操作被撤销得很值得庆祝"。
    /// </summary>
    private async void FavoriteButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AssetPageViewModel viewModel)
        {
            return;
        }

        await viewModel.ToggleFavoriteCommand.ExecuteAsync(null);

        if (!viewModel.IsFavorite)
        {
            return;
        }

        FavoriteBurst.Play();
        PlayIconPop();
    }

    /// <summary>
    /// 图标弹一下（scale 1 → 1.18 → 1）。
    /// 与烟花同步起手，但更短：图形先行、粒子随后弥散，读起来是一个动作而不是两个。
    /// </summary>
    private async void PlayIconPop()
    {
        if (_isIconPopping)
        {
            return;
        }

        _isIconPopping = true;

        try
        {
            FavoriteIcon.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            FavoriteIcon.Transitions = new Transitions
            {
                new TransformOperationsTransition
                {
                    Property = RenderTransformProperty,
                    Duration = TimeSpan.FromMilliseconds(120),
                    Easing = new BackEaseOut(),
                },
            };

            FavoriteIcon.RenderTransform = ScaledUp;
            await Task.Delay(120);

            // 回落用更长的时长 + EaseOut：上去要快（有冲击），下来要软（有分量）
            FavoriteIcon.Transitions = new Transitions
            {
                new TransformOperationsTransition
                {
                    Property = RenderTransformProperty,
                    Duration = TimeSpan.FromMilliseconds(200),
                    Easing = new CubicEaseOut(),
                },
            };
            FavoriteIcon.RenderTransform = Identity;
            await Task.Delay(200);
        }
        finally
        {
            _isIconPopping = false;
        }
    }
}
