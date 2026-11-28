using Avalonia.Controls;

namespace MarketAssistant.Views.Controls;

/// <summary>
/// 12 列内容栅格。
///
/// 设计系统 v3.0 规定内容区按 12 列组织、gutter 16。此前各页面用
/// <c>ColumnDefinitions="7*,5*"</c> 这类比例手写，列数不与 12 对齐，
/// 跨页面的栏宽无法保证一致。
///
/// 用法与普通 Grid 完全一致，只是列已预置：
/// <code>
/// &lt;controls:PageGrid ColumnSpacing="{StaticResource GridGutter}"&gt;
///     &lt;controls:CardView Grid.Column="0" Grid.ColumnSpan="7" /&gt;
///     &lt;controls:CardView Grid.Column="7" Grid.ColumnSpan="5" /&gt;
/// &lt;/controls:PageGrid&gt;
/// </code>
///
/// 列间距直接用 <see cref="Grid.ColumnSpacing"/>：它只插在相邻列之间（等价 CSS gap），
/// 首列贴左、末列贴右。此前的实现靠改写子元素 <c>Margin</c> 模拟 gutter，
/// 会把子元素自己写的上下外边距一起覆盖掉，属于不该有的副作用。
/// 也正因为间距由 Grid 负责，子元素不要再加左右 Margin。
/// </summary>
public class PageGrid : Grid
{
    /// <summary>栅格列数。12 是 v3 的基准，不建议改动。</summary>
    public const int ColumnCount = 12;

    /// <summary>默认 gutter，与 Spacing.axaml 的 <c>GridGutter</c> 一致；XAML 可用令牌覆盖。</summary>
    public const double DefaultGutter = 16;

    public PageGrid()
    {
        ColumnSpacing = DefaultGutter;

        for (var i = 0; i < ColumnCount; i++)
        {
            // 等宽星号列：子元素用 ColumnSpan 表达"占几列"，而不是改这里
            ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        }
    }
}
