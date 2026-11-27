using Avalonia;
using Avalonia.Controls;

namespace MarketAssistant.Views.Controls;

/// <summary>
/// <see cref="AssetCardView"/> 的骨架：真实卡片的结构克隆，热门页与自选页共用。
/// 热门卡比自选卡多"名次徽标"和"指标行"，用 <see cref="HasRank"/> / <see cref="HasMetric"/> 对齐，
/// 否则自选骨架会比真实卡片高一行。
/// </summary>
public partial class AssetCardSkeleton : UserControl
{
    /// <summary>表面形态，与对应页面里真实卡片的 <see cref="AssetCardView.Variant"/> 保持一致。</summary>
    public static readonly StyledProperty<AssetCardVariant> VariantProperty =
        AvaloniaProperty.Register<AssetCardSkeleton, AssetCardVariant>(nameof(Variant));

    /// <summary>是否预留名次徽标位。</summary>
    public static readonly StyledProperty<bool> HasRankProperty =
        AvaloniaProperty.Register<AssetCardSkeleton, bool>(nameof(HasRank));

    /// <summary>是否预留指标行。</summary>
    public static readonly StyledProperty<bool> HasMetricProperty =
        AvaloniaProperty.Register<AssetCardSkeleton, bool>(nameof(HasMetric));

    public AssetCardVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    public bool HasRank
    {
        get => GetValue(HasRankProperty);
        set => SetValue(HasRankProperty, value);
    }

    public bool HasMetric
    {
        get => GetValue(HasMetricProperty);
        set => SetValue(HasMetricProperty, value);
    }

    static AssetCardSkeleton()
    {
        VariantProperty.Changed.AddClassHandler<AssetCardSkeleton>((skeleton, _) => skeleton.ApplyVariant());
    }

    public AssetCardSkeleton()
    {
        InitializeComponent();
        ApplyVariant();
    }

    private void ApplyVariant()
    {
        Surface.Variant = Variant == AssetCardVariant.Card ? SkeletonCardVariant.Card : SkeletonCardVariant.Tile;
    }
}
