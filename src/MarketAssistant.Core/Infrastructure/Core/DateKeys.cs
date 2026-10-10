namespace MarketAssistant.Infrastructure.Core;

/// <summary>
/// 日期键（<c>yyyy-MM-dd</c> 定长零填充文本）的单一来源。
/// </summary>
/// <remarks>
/// <para>
/// 日期键在多处充当持久化分组键：交易日的日统计/账户快照、每日简报的当天缓存、
/// 知识图谱关系的生效/截止默认值。此前三处各自内联
/// <see cref="DateTime.Now"/> / <see cref="DateTime.UtcNow"/> + 格式串，带来两个问题：
/// ① 文化不统一——交易仓储一处未指定 <see cref="System.Globalization.CultureInfo"/>，
/// 在非拉丁数字区域下日期键可能含本地数字，SQLite 的字典序比较随之错乱；
/// ② 时区不统一——知识图谱用 UTC、交易与简报用本地，"今天"的边界相差最多 24 小时。
/// </para>
/// <para>
/// <b>统一约定：一律本地时间。</b>日期键是"用户感知的那一天"——
/// 交易日切分、简报当天缓存、图谱默认生效日都以用户所在时区为准；
/// UTC 只用于时间戳（<c>ToString("O")</c> 往返格式），不用于日期键。
/// 格式串钉 InvariantCulture：定长零填充保证字典序即时序，数字不受区域影响。
/// </para>
/// </remarks>
public static class DateKeys
{
    /// <summary>当天日期键（本地时间）。</summary>
    public static string Today() => DateTime.Now.ToString(Format, Culture);

    /// <summary>指定日期的日期键（按该 DateTime 的面值取日期部分，不做时区换算）。</summary>
    public static string From(DateTime date) => date.ToString(Format, Culture);

    private const string Format = "yyyy-MM-dd";

    private static readonly System.Globalization.CultureInfo Culture =
        System.Globalization.CultureInfo.InvariantCulture;
}
