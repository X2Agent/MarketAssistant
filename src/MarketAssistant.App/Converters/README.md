# Avalonia 值转换器

本目录包含项目中使用的 Avalonia 值转换器（IValueConverter/IMultiValueConverter）。

---

## 转换器列表

### 1. PriceChangeColorConverter

根据价格变化值自动选择对应的颜色（中国股市配色习惯，红涨绿跌）。颜色读取设计系统资源（`BullishBrush` / `BearishBrush` / `NeutralBrush`，即 `Colors.axaml` 中的 `BullishRed #F44336` / `BearishGreen #4CAF50` / `Neutral #9E9E9E`），并带兜底常量：

- 上涨：`BullishBrush`（红 `#F44336`）
- 下跌：`BearishBrush`（绿 `#4CAF50`）
- 无变化/无效值：`NeutralBrush`（灰 `#9E9E9E`）

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

### 4. NullableValueConverter

可空值格式化转换器，参数格式 `format|fallback`（如 `"{0:F2}元|--"`）。

### 5. NullableVisibilityConverter

当值为 null 时返回 false，用于 `IsVisible` 绑定。

### 6. ScoreToColorConverter

将评分（1-10）映射到对应的颜色，用于分析报告评分展示。

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
