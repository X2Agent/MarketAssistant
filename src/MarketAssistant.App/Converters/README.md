# Avalonia 值转换器

本目录包含项目中使用的 Avalonia 值转换器（IValueConverter/IMultiValueConverter）。

---

## 转换器列表

### 1. PriceChangeColorConverter

根据价格变化值自动选择对应的颜色（中国股市配色习惯）：
- 上涨：红色 `#e74c3c`
- 下跌：绿色 `#2ecc71`
- 无变化/无效值：灰色 `#6c757d`

```xml
<TextBlock Text="{Binding PriceChange}"
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
