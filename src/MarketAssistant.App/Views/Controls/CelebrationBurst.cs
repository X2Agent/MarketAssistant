using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace MarketAssistant.Views.Controls;

/// <summary>
/// 收藏成功的粒子扩散：约 12 粒光点从中心径向散开，520ms 后自动清空。
///
/// 动效性格 C（表达情绪），只用于"用户主动触发且结果可逆"的动作（见 Motion.axaml）。
/// 全库仅资产详情页的「加入自选」使用：首页热门资产的收藏会先弹确认框再弹成功框，
/// 烟花会被模态窗压住、延迟到关窗后才播；自选页只有删除没有新增。
///
/// 用单次 <see cref="Render"/> + 一个 16ms 定时器驱动，而不是挂 12 套 Animation：
/// 粒子的位置/透明度每帧都在变，用动画系统反而要生成大量过渡对象。
/// </summary>
public class CelebrationBurst : Control
{
    private struct Particle
    {
        public double Angle;
        public double Speed;
        public double Radius;
        public double Offset;
    }

    private const double DragPerSecond = 2.4;
    private const double Gravity = 120;

    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(16);

    private Particle[] _particles = [];
    private DispatcherTimer? _timer;
    private TimeSpan _elapsed;

    public CelebrationBurst()
    {
        IsHitTestVisible = false;
        IsVisible = false;
    }

    /// <summary>从控件中心播放一次扩散。重复调用会重置进度。</summary>
    public void Play()
    {
        var count = Math.Max(1, MotionTokens.BurstParticleCount);
        _particles = new Particle[count];

        // 角度均匀分布 + 半格抖动，避免出现"十字形"的机械感
        for (var i = 0; i < count; i++)
        {
            _particles[i] = new Particle
            {
                Angle = (Math.PI * 2 / count) * i + Random.Shared.NextDouble() * 0.35,
                Speed = 90 + Random.Shared.NextDouble() * 90,
                Radius = 2 + Random.Shared.NextDouble() * 1.8,
                Offset = Random.Shared.NextDouble() * -0.12,
            };
        }

        _elapsed = TimeSpan.Zero;
        IsVisible = true;

        _timer ??= new DispatcherTimer { Interval = FrameInterval };
        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();

        InvalidateVisual();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _elapsed += FrameInterval;

        if (_elapsed >= MotionTokens.BurstDuration)
        {
            Stop();
            return;
        }

        InvalidateVisual();
    }

    private void Stop()
    {
        _timer?.Stop();
        if (_timer is not null)
        {
            _timer.Tick -= OnTick;
        }

        _particles = [];
        IsVisible = false;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (_particles.Length == 0)
        {
            return;
        }

        var progress = Math.Clamp(_elapsed.TotalMilliseconds / MotionTokens.BurstDuration.TotalMilliseconds, 0, 1);
        var seconds = _elapsed.TotalSeconds;

        // 颜色跟随唯一强调色：v3 允许烟花是"表达"，但不允许引入第二个强调色或语义色装饰
        var brush = (IBrush)this.FindResource("AccentBrush")!;

        // 先整体散开、后半程才开始变淡：粒子在最强时清晰可见，尾部才消融
        var opacity = progress < 0.45 ? 1 : 1 - (progress - 0.45) / 0.55;
        if (opacity <= 0)
        {
            return;
        }

        // 发射点取控件中心（Bounds 每帧现算）：数据绑定会让按钮宽度在
        // "加入自选" 与 "已自选" 之间变化，缓存尺寸会在换文案后打偏。
        var origin = new Point(Bounds.Width / 2, Bounds.Height / 2);

        using var _ = context.PushOpacity(opacity);

        for (var i = 0; i < _particles.Length; i++)
        {
            var particle = _particles[i];

            // 匀速 + 二次衰减的简易阻尼，比真解微分方程更像"烟花"而不是"爆炸"
            var distance = particle.Speed * seconds * (1 - DragPerSecond * seconds / 2);
            var dx = Math.Cos(particle.Angle) * distance;
            var dy = Math.Sin(particle.Angle) * distance + 0.5 * Gravity * particle.Offset * seconds * seconds;
            var radius = particle.Radius * (1 - progress * 0.5);

            if (radius <= 0.2)
            {
                continue;
            }

            context.DrawEllipse(brush, null, new Point(origin.X + dx, origin.Y + dy), radius, radius);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
        if (_timer is not null)
        {
            _timer.Tick -= OnTick;
            _timer = null;
        }

        _particles = [];
    }
}
