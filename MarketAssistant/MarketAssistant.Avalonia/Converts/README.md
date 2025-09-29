# Avalonia Converters

本目录包含从MAUI项目转换而来的Avalonia值转换器。

## 转换器列表

### 1. BoolToColorConverter
- **功能**: 将布尔值转换为颜色（Brush）
- **用法**: 支持参数化颜色配置，格式为 "TrueColor,FalseColor"
- **默认**: true=绿色，false=红色，null=灰色

### 2. DictionaryToStringConverter
- **功能**: 字典与字符串的双向转换
- **格式**: 每行一个键值对，默认分隔符为 "="
- **支持**: 自定义分隔符通过参数传递

### 3. DoubleConverter
- **功能**: 整数值与0-1范围浮点数的双向转换
- **用途**: 主要用于进度条显示（0-100整数 ↔ 0-1浮点数）

### 4. PriceChangeColorConverter
- **功能**: 价格变化值转换为对应颜色
- **支持类型**: decimal数值和百分比字符串（如"+1.26%"）
- **颜色规则**: 
  - 上涨：红色 (#e74c3c)
  - 下跌：绿色 (#2ecc71)
  - 无变化：灰色 (#6c757d)

### 5. RadioButtonEqualityConverter
- **功能**: RadioButton双向绑定转换器
- **用途**: 将字符串值与RadioButton的IsChecked属性进行双向绑定
- **特性**: 未选中时使用BindingOperations.DoNothing避免界面刷新问题

## 主要变更

从MAUI转换到Avalonia的主要变更：

1. **命名空间**: 
   - `Microsoft.Maui.Controls` → `Avalonia.Data.Converters`
   - `Microsoft.Maui.Graphics` → `Avalonia.Media`

2. **颜色处理**:
   - `Colors.Green` → `Brushes.Green` 或 `new SolidColorBrush(Color.FromRgb(...))`
   - 支持颜色字符串解析：`Brush.Parse()`

3. **绑定操作**:
   - `Binding.DoNothing` → `BindingOperations.DoNothing`

4. **基类简化**:
   - 移除了MAUI的`BaseConverter`依赖，直接实现`IValueConverter`

所有转换器都保持了原有的功能逻辑，只是适配了Avalonia的API。
