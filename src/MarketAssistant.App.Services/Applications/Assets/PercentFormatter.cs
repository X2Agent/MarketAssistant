namespace MarketAssistant.Applications.Assets;

/// <summary>
/// 涨跌幅展示格式化工具：统一「带符号百分比」的格式与文化约定。
/// </summary>
/// <remarks>
/// <para>
/// 本项目多处需要把涨跌幅比率/百分数渲染为带显式正负号的文本
/// （顶栏行情条、资产列表、告警规则）。此前各点内联
/// <c>+0.00;-0.00;0.00</c> 自定义格式串，存在两个隐患：
/// ① 任一处手滑写成 <c>F2</c> 就会丢正负号，而正负号是涨跌配色（红涨绿跌）的上游依据；
/// ② 文化不一致——部分点钉 <see cref="System.Globalization.CultureInfo.InvariantCulture"/>，
/// 部分点随当前区域插值，而 <c>AssetInfo.ChangePercentage</c> 的下游
/// <c>PriceChangeColorConverter</c> 以 InvariantCulture 解析该字符串。
/// </para>
/// <para>
/// 因此本类统一钉 InvariantCulture：数字与符号在任何区域下逐字节一致，
/// 与解析侧契约对齐。中文市场下 Latin 数字的展示不受影响。
/// </para>
/// </remarks>
public static class PercentFormatter
{
    /// <summary>
    /// 格式化百分数（两位小数，带显式符号，零值无符号）：<c>+1.23%</c> / <c>-0.82%</c> / <c>0.00%</c>。
    /// </summary>
    /// <param name="percent">百分数值（如 1.23 表示 +1.23%，非比率）。</param>
    public static string Format(decimal percent) =>
        percent.ToString("+0.00;-0.00;0.00", System.Globalization.CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// <see cref="Format(decimal)"/> 的 double 重载：供上游解析结果为 double 的数据源
    /// （如新浪资金流接口）使用，避免 decimal 转换引入的舍入差异。
    /// </summary>
    public static string Format(double percent) =>
        percent.ToString("+0.00;-0.00;0.00", System.Globalization.CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// 格式化带符号数值（两位小数，无百分号）：<c>+1.23</c> / <c>-0.82</c> / <c>0.00</c>。
    /// 供「个百分点」等不带 % 的场景使用。
    /// </summary>
    public static string FormatValue(decimal value) =>
        value.ToString("+0.00;-0.00;0.00", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// 格式化带符号数值（自适应精度，去掉多余的尾零）：<c>+5</c> / <c>-1.25</c> / <c>0</c>。
    /// 供告警规则浮动值等「用户输入多少位就展示多少位」的场景使用。
    /// </summary>
    public static string FormatCompact(decimal value) =>
        value.ToString("+0.########;-0.########;0", System.Globalization.CultureInfo.InvariantCulture);
}
