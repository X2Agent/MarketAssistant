# Avalonia 值转换器

本目录包含项目中使用的 Avalonia 值转换器（IValueConverter/IMultiValueConverter）。

---

## 转换器列表

### 1. PriceChangeColorConverter

根据价格变化值自动选择对应的颜色（中国股市配色习惯，红涨绿跌）。颜色读取设计系统资源（`BullishBrush` / `BearishBrush` / `NeutralBrush`，即 `Colors.axaml` 中的 `BullishRed #E5484D` / `BearishGreen #16A34A` / `Neutral #8A93A0`），并带兜底常量：

- 上涨：`BullishBrush`（红 `#E5484D`）
- 下跌：`BearishBrush`（绿 `#16A34A`）
- 无变化/无效值：`NeutralBrush`（灰 `#8A93A0`）

支持 `ConverterParameter=tag` 返回约 12% 透明度的同色标签背景画刷（`BullishTagBackgroundBrush` 等），用于涨跌标签色块样式。

```xml
<TextBlock Text="{Binding PriceChange}"
           Foreground="{Binding PriceChange, Converter={StaticResource PriceChangeColorConverter}}" />

<!-- 涨跌标签：12% 同色背景 + 同色文字 -->
<TextBlock Text="{Binding PriceChange}"
           Background="{Binding PriceChange, Converter={StaticResource PriceChangeColorConverter}, ConverterParameter=tag}"
           Foreground="{Binding PriceChange, Converter={StaticResource PriceChangeColorConverter}}" />
```

### 2. RadioButtonEqualityConverter

用于 RadioButton 的 IsChecked 属性与字符串值的双向绑定。

```xml
<RadioButton IsChecked="{Binding ServerType,
                Converter={StaticResource RadioButtonEqualityConverter},
                ConverterParameter=sse,
                Mode=TwoWay}" />
```

### 3. EnumDescriptionConverter

将枚举值转换为 `[Description]` 特性中指定的描述文本，用于 UI 显示。

> `NullableValueConverter`、`NullableVisibilityConverter` 已删除：可空值的展示文案由 ViewModel 直接给出
> （空值统一显示"—"），可见性用 Avalonia 内置的 `ObjectConverters.IsNotNull` / `StringConverters.IsNotNullOrEmpty`。

> `ScoreToColorConverter` 已删除。它把评分映射成红/橙/黄/绿五档色值（`#10B981` 等），
> 这些色值不在 `Colors.axaml` 里，暗色模式也没算过对比度，且一屏同时出现评级实底色块、
> 四色分数、四色进度条会打散唯一强调色焦点。评分现在只用主文字 + 等宽数字表达，
> 语义色仅保留给评级徽章（买/持有/卖）与涨跌数值。

---

## Avalonia 内置转换器参考

优先使用 Avalonia 内置转换器，减少自定义代码：

```xml
<!-- 布尔反转 -->
<TextBlock IsVisible="{Binding IsLoading, Converter={x:Static BoolConverters.Not}}" />

<!-- 字符串非空 -->
<TextBlock IsVisible="{Binding Name, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />

<!-- 对象非空 -->
<TextBlock IsVisible="{Binding Data, Converter={x:Static ObjectConverters.IsNotNull}}" />
```
