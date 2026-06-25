# Avalonia 值转换器

本目录包含项目中使用的 Avalonia 值转换器（IValueConverter/IMultiValueConverter）。

---

## 📋 转换器列表

### 1. PriceChangeColorConverter（价格变化颜色转换器）⭐

**功能说明**：
- 根据价格变化值自动选择对应的颜色（中国股市配色习惯）
- 支持 `decimal` 数值和百分比字符串（如 "+1.26%"、"-0.83%"）

**颜色规则**：
- 上涨：红色 `#e74c3c`
- 下跌：绿色 `#2ecc71`
- 无变化/无效值：灰色 `#6c757d`

**使用场景**：
- 股票价格涨跌显示
- 市场数据变化趋势展示

**实际使用位置**：
- `StockPageView.axaml` - 股票详情页价格显示
- `HomePageView.axaml` - 首页市场概览
- `FavoritesPageView.axaml` - 自选股列表

**示例代码**：
```xml
<TextBlock Text="{Binding PriceChange}" 
           Foreground="{Binding PriceChange, Converter={StaticResource PriceChangeColorConverter}}" />
```

**Avalonia 内置替代方案**：无，这是业务逻辑相关的自定义转换器。

---

### 2. NavigationIconConverter（导航图标转换器）⭐

**功能说明**：
- 根据导航项的选中状态返回对应的 SVG 图标路径
- 实现了 `IMultiValueConverter`，需要两个输入值：导航项对象和选中状态

**使用场景**：
- 侧边栏导航菜单的图标切换（选中/未选中状态）

**实际使用位置**：
- `MainWindow.axaml` - 主窗口导航栏

**示例代码**：
```xml
<MultiBinding Converter="{StaticResource NavigationIconConverter}">
    <Binding Path="." />
    <Binding Path="IsSelected" />
</MultiBinding>
```

**Avalonia 内置替代方案**：无，这是业务逻辑相关的自定义转换器。

---

### 3. RadioButtonEqualityConverter（单选按钮相等性转换器）⭐

**功能说明**：
- 用于 RadioButton 的 IsChecked 属性与字符串值的双向绑定
- Convert：判断源值是否等于参数值，返回布尔值
- ConvertBack：如果选中则返回参数值，未选中则返回 `BindingOperations.DoNothing`（避免不必要的更新）

**使用场景**：
- 多个 RadioButton 绑定到同一个字符串属性
- 枚举值选择器

**实际使用位置**：
- `MCPConfigPageView.axaml` - MCP 服务器类型选择（stdio/sse）

**示例代码**：
```xml
<RadioButton Content="SSE" 
             IsChecked="{Binding ServerType, 
                         Converter={StaticResource RadioButtonEqualityConverter}, 
                         ConverterParameter=sse, 
                         Mode=TwoWay}" />
```

**Avalonia 内置替代方案**：无，这是常见的 RadioButton 绑定模式，需要自定义实现。

---

## 📚 Avalonia 内置转换器参考

Avalonia 提供了一些常用的内置转换器，项目中已使用的有：

### BoolConverters（布尔值转换器）✅ 已使用

**命名空间**：`Avalonia.Data.Converters`（无需引入，可直接使用）

**项目中的使用**：
- `ChatSidebarView.axaml` - 控制消息显示和输入框启用状态
- `AgentAnalysisPageView.axaml` - 控制分析报告区域显示
- `MCPConfigPageView.axaml` - 控制未选择服务器时的提示
- `AboutPageView.axaml` - 控制更新按钮状态

```xml
<!-- 布尔反转（项目中使用） -->
<TextBlock IsVisible="{Binding IsLoading, Converter={x:Static BoolConverters.Not}}" />
<Button IsEnabled="{Binding IsProcessing, Converter={x:Static BoolConverters.Not}}" />

<!-- 其他可用的布尔转换器 -->
<!-- 与运算 -->
<Button IsEnabled="{Binding IsValid, Converter={x:Static BoolConverters.And}, ConverterParameter=...}" />

<!-- 或运算 -->
<Button IsVisible="{Binding IsAdmin, Converter={x:Static BoolConverters.Or}, ConverterParameter=...}" />
```

### StringConverters（字符串转换器）
```xml
<!-- 字符串为空或空白 -->
<TextBlock IsVisible="{Binding Name, Converter={x:Static StringConverters.IsNullOrEmpty}}" />

<!-- 字符串非空 -->
<TextBlock IsVisible="{Binding Name, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />
```

### ObjectConverters（对象转换器）
```xml
<!-- 对象为空 -->
<TextBlock IsVisible="{Binding Data, Converter={x:Static ObjectConverters.IsNull}}" />

<!-- 对象非空 -->
<TextBlock IsVisible="{Binding Data, Converter={x:Static ObjectConverters.IsNotNull}}" />
```

---

## 🔄 迁移说明

本项目从 MAUI 迁移到 Avalonia 时的主要变更：

### 命名空间变更
- `Microsoft.Maui.Controls` → `Avalonia.Data.Converters`
- `Microsoft.Maui.Graphics` → `Avalonia.Media`

### API 变更
- `Colors.Green` → `Brushes.Green` 或 `new SolidColorBrush(Color.FromRgb(...))`
- `Binding.DoNothing` → `BindingOperations.DoNothing`
- 颜色字符串解析：`Brush.Parse("#e74c3c")`

### 基类变更
- 移除 MAUI 的 `BaseConverter` 依赖
- 直接实现 `IValueConverter` 或 `IMultiValueConverter`

---

## 📝 最佳实践

1. **优先使用 Avalonia 内置转换器**：减少自定义代码，提高可维护性
2. **避免在转换器中执行复杂业务逻辑**：转换器应该只做数据转换
3. **为自定义转换器添加完整的文档注释**：说明输入输出类型和参数
4. **支持 null 值处理**：避免转换器抛出异常
5. **ConvertBack 不支持时应返回 `BindingOperations.DoNothing`**：而不是抛出异常

---

## ✅ 代码优化历史

### 第一轮清理（已完成）
1. **删除未使用的转换器**：
   - ✅ `BoolToColorConverter.cs` - 已删除
   - ✅ `DictionaryToStringConverter.cs` - 已删除
   - ✅ `DoubleConverter.cs` - 已删除

2. **合并重复的转换器**：
   - ✅ `InvertBoolConverter.cs` - 已删除（功能与 InvertedBoolConverter 重复）

### 第二轮优化（已完成）
3. **使用 Avalonia 内置转换器**：
   - ✅ `InvertedBoolConverter.cs` - 已删除，全部替换为 `{x:Static BoolConverters.Not}`
   - ✅ 更新了所有视图文件的转换器引用（4个文件，6处使用）

**优化成果**：
- 从 9 个转换器文件减少到 **3 个**
- 代码更简洁，可维护性更高
- 充分利用了 Avalonia 内置功能，减少了自定义代码
