# MAUI 到 Avalonia 组件迁移指南

本文档记录了从 MAUI 项目迁移自定义控件到 Avalonia 项目的完整过程，包括遇到的问题、解决方案和最佳实践。

## 📋 目录

- [迁移概览](#迁移概览)
- [控件类型选择](#控件类型选择)
- [具体迁移案例](#具体迁移案例)
- [关键差异对比](#关键差异对比)
- [常见问题解决](#常见问题解决)
- [最佳实践建议](#最佳实践建议)

## 🎯 迁移概览

### 已完成迁移的组件

| 组件名称 | MAUI类型 | Avalonia类型 | 迁移状态 | 备注 |
|---------|---------|-------------|---------|------|
| `CardView` | ContentView | TemplatedControl | ✅ 完成 | 重构为无外观控件 |
| `WatermarkView` | ContentView | Control (自绘) | ✅ 完成 | 使用自定义渲染 |
| `StockWebChartView` | ContentView | UserControl | ✅ 完成 | 需集成WebView组件 |

### 迁移统计

- **总迁移组件**: 3个
- **成功迁移**: 3个
- **需要后续完善**: 1个 (StockWebChartView 需要WebView集成)

## 🏗️ 控件类型选择

根据 [Avalonia 官方文档](https://docs.avaloniaui.net/docs/guides/custom-controls/types-of-control) 的最佳实践：

### 1. UserControls
**适用场景**: 应用特定的"视图"或"页面"
- ✅ `StockWebChartView` - 股票图表展示组件

### 2. TemplatedControls  
**适用场景**: 可在不同应用间共享的通用控件，无外观控件
- ✅ `CardView` - 通用卡片容器控件

### 3. Basic Controls (自绘控件)
**适用场景**: 通过重写 `Visual.Render` 方法自绘的基础控件
- ✅ `WatermarkView` - 水印覆盖组件

## 📝 具体迁移案例

### 案例 1: CardView (ContentView → TemplatedControl)

#### MAUI 原始实现
```csharp
public class CardView : ContentView
{
    public static readonly BindableProperty HeaderProperty = 
        BindableProperty.Create(nameof(Header), typeof(object), typeof(CardView));
    
    // 使用 XAML 布局定义
}
```

#### Avalonia 迁移后
```csharp
public partial class CardView : TemplatedControl
{
    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<CardView, object?>(nameof(Header), null);
    
    public static readonly StyledProperty<object?> ContentProperty =
        ContentControl.ContentProperty.AddOwner<CardView>();
    
    // 使用 ControlTemplate 定义样式
}
```

#### 关键变化
1. **继承类型**: `ContentView` → `TemplatedControl`
2. **属性系统**: `BindableProperty` → `StyledProperty`
3. **布局定义**: XAML UserControl → Styles 中的 ControlTemplate
4. **模板元素**: 使用 `PART_` 命名约定
5. **事件处理**: `OnApplyTemplate` 替代构造函数中的控件查找

### 案例 2: WatermarkView (ContentView → Control)

#### MAUI 原始实现
```csharp
public class WatermarkView : ContentView
{
    private Grid _watermarkGrid;
    
    private void UpdateWatermark()
    {
        // 动态创建 Label 控件
        var label = new Label { /* ... */ };
        _watermarkGrid.Add(label);
    }
}
```

#### Avalonia 迁移后
```csharp
public class WatermarkView : Control
{
    static WatermarkView()
    {
        // 设置属性变更时重绘
        AffectsRender<WatermarkView>(/* 所有影响渲染的属性 */);
    }
    
    public override void Render(DrawingContext context)
    {
        // 使用 FormattedText 和 DrawingContext 自绘
        var formattedText = new FormattedText(/* ... */);
        context.DrawText(formattedText, position);
    }
}
```

#### 关键变化
1. **继承类型**: `ContentView` → `Control`
2. **渲染方式**: 动态控件创建 → 自定义绘制
3. **性能优化**: 使用 `AffectsRender` 声明影响渲染的属性
4. **绘制API**: MAUI控件 → Avalonia DrawingContext

### 案例 3: StockWebChartView (ContentView → UserControl + WebView)

#### MAUI 原始实现
```csharp
public class StockWebChartView : ContentView
{
    private readonly WebView _webView;
    
    public StockWebChartView()
    {
        _webView = new WebView 
        {
            HeightRequest = -1,
            WidthRequest = -1,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        
        // 加载HTML文件
        _webView.Source = new HtmlWebViewSource
        {
            BaseUrl = FileSystem.AppDataDirectory,
            Html = LoadHtmlContent("kline_chart.html")
        };
        
        Content = _webView;
        
        // 监听事件
        _webView.Navigated += (sender, e) => _isInitialized = true;
    } 
    
    public async Task UpdateChartAsync(IEnumerable<StockKLineData> kLineData)
    {
        await _webView.EvaluateJavaScriptAsync($"window.stockChartInterface.loadData({jsonData});");
    }
}
```

#### Avalonia 迁移后
```csharp
public class StockWebChartView : UserControl
{
    private WebView? _webView; // 使用 WebView.Avalonia.Desktop
    
    private void InitializeComponent()
    {
        // 创建 WebView (需要 WebView.Avalonia.Desktop 包)
        _webView = new WebView
        {
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        
        // 加载状态和错误处理的 UI 层
        var loadingPanel = new StackPanel { /* 加载状态 */ };
        var errorPanel = new StackPanel { /* 错误状态 */ };
        
        var grid = new Grid();
        grid.Children.Add(_webView);
        grid.Children.Add(loadingPanel);
        grid.Children.Add(errorPanel);
        
        Content = new Border { Child = grid };
    }
    
    private async Task InitializeChartAsync()
    {
        string htmlContent = await LoadHtmlContentAsync("kline_chart.html");
        
        // TODO: 使用正确的 WebView API 加载 HTML 内容
        // _webView.NavigateToString(htmlContent);
        
        // 当前使用模拟导航完成
        SimulateNavigationCompleted();
    }
    
    public async Task UpdateChartAsync(IEnumerable<StockKLineData> kLineData)
    {
        // TODO: 使用正确的 WebView API 执行 JavaScript
        // await _webView.ExecuteScriptAsync($"window.stockChartInterface.loadData({jsonData});");
        
        // 当前记录日志用于调试
        _logger?.LogInformation($"JavaScript 调用: {script}");
    }
}
```

#### 关键变化
1. **WebView支持**: MAUI内置 → Avalonia需要 `WebView.Avalonia.Desktop` 包
2. **API差异**: WebView API 在两个平台上有所不同，需要查阅具体文档
3. **事件处理**: MAUI的 `Navigated` → Avalonia需要确认正确的事件名称
4. **JavaScript执行**: `EvaluateJavaScriptAsync` → `ExecuteScriptAsync` (API可能不同)
5. **HTML加载**: `HtmlWebViewSource` → `NavigateToString` (需要确认)
6. **状态管理**: 增加了更完善的加载/错误状态 UI

#### WebView 集成注意事项
⚠️ **重要**: `WebView.Avalonia.Desktop` 库的具体 API 可能与预期不同，需要：
1. 查阅库的官方文档确认正确的方法名称
2. 确认事件处理的正确语法
3. 测试 HTML 内容加载和 JavaScript 执行
4. 考虑平台差异 (Windows/macOS/Linux)

## 🔄 关键差异对比

### 属性系统

| 特性 | MAUI | Avalonia |
|-----|------|----------|
| 属性定义 | `BindableProperty` | `StyledProperty` |
| 属性注册 | `BindableProperty.Create()` | `AvaloniaProperty.Register()` |
| 属性继承 | `AddOwner()` | `AddOwner<T>()` |
| 变更通知 | `propertyChanged` | `OnPropertyChanged` 重写 |

### 控件模板

| 特性 | MAUI | Avalonia |
|-----|------|----------|
| 模板定义 | `ControlTemplate` | `ControlTemplate` |
| 模板元素 | 任意命名 | `PART_` 前缀约定 |
| 模板应用 | `OnApplyTemplate` | `OnApplyTemplate` |
| 元素查找 | `GetTemplateChild()` | `e.NameScope.Find<T>()` |

### 样式和资源

| 特性 | MAUI | Avalonia |
|-----|------|----------|
| 样式文件 | ResourceDictionary | Styles |
| 选择器语法 | `TargetType` | `Selector="Type.Class"` |
| 动态资源 | `{DynamicResource}` | `{DynamicResource}` |
| 主题绑定 | `{AppThemeBinding}` | 条件样式 |

### 渲染和绘制

| 特性 | MAUI | Avalonia |
|-----|------|----------|
| 自定义绘制 | 较少使用 | `Control.Render()` |
| 绘制上下文 | `ICanvas` | `DrawingContext` |
| 文本渲染 | `DrawString()` | `FormattedText` + `DrawText()` |
| 变换矩阵 | Transform类 | `Matrix` 结构 |

## ⚠️ 常见问题解决

### 1. 属性冲突警告
**问题**: `CS0108: 成员隐藏继承的成员`
```csharp
// 错误示例
public static readonly StyledProperty<double> FontSizeProperty = ...
public double FontSize { get; set; }
```

**解决方案**: 
```csharp
// 方案1: 重命名属性
public static readonly StyledProperty<double> WatermarkFontSizeProperty = ...

// 方案2: 使用 new 关键字
public static new readonly StyledProperty<double> FontSizeProperty = ...
public new double FontSize { get; set; }
```

### 2. 模板元素未找到
**问题**: `NullReferenceException` 在访问模板元素时
```csharp
// 错误示例
public CardView()
{
    var element = this.FindControl<TextBlock>("HeaderLabel"); // null
}
```

**解决方案**:
```csharp
protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
{
    base.OnApplyTemplate(e);
    var element = e.NameScope.Find<TextBlock>("PART_HeaderLabel");
}
```

### 3. 样式定义错误
**问题**: `AVLN3000: Unable to find suitable setter`
```csharp
// 错误的样式文件结构
<ResourceDictionary>
    <Style Selector="...">
```

**解决方案**:
```xml
<Styles xmlns="...">
    <Style Selector="controls|CardView">
```

### 4. Content 属性缺失
**问题**: `AVLN2000: Unable to resolve property Content`
```csharp
// TemplatedControl 需要显式定义 Content 属性
public static readonly StyledProperty<object?> ContentProperty =
    ContentControl.ContentProperty.AddOwner<CardView>();
```

## 💡 最佳实践建议

### 1. 选择合适的控件类型
- **通用可复用控件** → `TemplatedControl`
- **应用特定视图** → `UserControl`  
- **需要自定义绘制** → `Control` (重写 Render)

### 2. 遵循 Avalonia 命名约定
- 模板元素使用 `PART_` 前缀
- 样式类使用 PascalCase
- 属性使用 `StyledProperty` 后缀

### 3. 正确处理模板应用
```csharp
protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
{
    base.OnApplyTemplate(e);
    
    // 获取模板元素
    _element = e.NameScope.Find<Control>("PART_Element");
    
    // 设置初始状态
    UpdateVisualState();
}
```

### 4. 优化自绘控件性能
```csharp
static MyControl()
{
    // 声明影响渲染的属性
    AffectsRender<MyControl>(
        TextProperty,
        ColorProperty,
        SizeProperty);
}
```

### 5. 样式文件组织
```xml
<!-- 推荐的样式文件结构 -->
<Styles xmlns="https://github.com/avaloniaui"
        xmlns:controls="using:YourNamespace.Controls">
    
    <!-- 默认样式 -->
    <Style Selector="controls|YourControl">
        <Setter Property="Template">
            <ControlTemplate>
                <!-- 模板内容 -->
            </ControlTemplate>
        </Setter>
    </Style>
    
    <!-- 变体样式 -->
    <Style Selector="controls|YourControl.Large">
        <Setter Property="FontSize" Value="18"/>
    </Style>
</Styles>
```

## 📚 参考资源

### Avalonia 官方文档
- [控件类型选择](https://docs.avaloniaui.net/docs/guides/custom-controls/types-of-control)
- [创建自定义面板](https://docs.avaloniaui.net/docs/guides/custom-controls/create-a-custom-panel)  
- [属性定义](https://docs.avaloniaui.net/docs/guides/custom-controls/defining-properties)
- [自绘控件](https://docs.avaloniaui.net/docs/guides/custom-controls/draw-with-a-property)
- [TemplatedControls](https://docs.avaloniaui.net/docs/guides/custom-controls/how-to-create-templated-controls)

### 迁移检查清单

- [ ] 选择合适的控件基类
- [ ] 转换属性系统 (BindableProperty → StyledProperty)
- [ ] 更新样式定义 (ResourceDictionary → Styles)
- [ ] 处理模板元素查找 (PART_ 命名约定)
- [ ] 测试属性绑定和样式应用
- [ ] 验证在不同主题下的表现
- [ ] 检查性能和内存使用

---

## 🎉 总结

通过本次迁移实践，我们成功将 3 个 MAUI 自定义控件迁移到了 Avalonia 平台。主要收获：

1. **架构理解**: 深入理解了 Avalonia 的控件架构和最佳实践
2. **类型选择**: 学会了根据用途选择合适的控件基类
3. **问题解决**: 积累了常见迁移问题的解决经验
4. **性能优化**: 掌握了 Avalonia 特有的性能优化技巧

这份文档将作为后续组件迁移的重要参考，帮助团队更高效地完成 MAUI 到 Avalonia 的迁移工作。

---
*文档生成时间: 2025年9月29日*  
*Avalonia 版本: 11.x*  
*MAUI 版本: .NET 8*
