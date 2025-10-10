# StockPage 迁移总结

## 📋 迁移概览

从 MAUI 的 `StockPage.xaml` 成功迁移到 Avalonia 的 `StockPageView.axaml`

---

## 🎯 页面功能

股票详情页面，主要功能包括：
1. **股票基本信息显示**：名称、代码、当前价格、涨跌幅
2. **K线图表展示**：支持分时、日K、周K、月K切换
3. **AI分析导航**：跳转到股票分析页面
4. **数据刷新**：手动刷新K线数据
5. **错误处理**：友好的错误提示

---

## 🔄 主要变更

### 1. ViewModel变更

#### MAUI版本 (`StockViewModel.cs`)
```csharp
[QueryProperty(nameof(StockCode), "code")]
public partial class StockViewModel : ViewModelBase
{
    private async void NavigateToAnalysisAsync()
    {
        await Shell.Current.GoToAsync("analysis", new Dictionary<string, object>
        {
            { "code", StockCode }
        });
    }
}
```

#### Avalonia版本 (`StockPageViewModel.cs`)
```csharp
public partial class StockPageViewModel : ViewModelBase
{
    // 使用消息机制进行导航
    private void NavigateToAnalysisAsync()
    {
        WeakReferenceMessenger.Default.Send(new NavigationMessage("Analysis", 
            new Dictionary<string, object> { { "code", StockCode } }
        ));
    }

    // 提供设置股票代码的方法
    public void SetStockCode(string code)
    {
        StockCode = code;
        if (!string.IsNullOrEmpty(code))
        {
            _ = LoadStockDataAsync(code);
        }
    }
}
```

**变更说明**:
- ❌ 移除 `[QueryProperty]` 特性（Avalonia不支持）
- ✅ 添加 `SetStockCode` 方法用于外部设置代码
- ✅ 使用 `WeakReferenceMessenger` 进行页面导航
- ✅ 移除 `Shell.Current.GoToAsync`，改用消息传递

---

### 2. View变更

#### 布局结构对比

| 元素 | MAUI | Avalonia |
|------|------|----------|
| 根容器 | `ContentPage` | `UserControl` |
| 卡片容器 | `Border` | `controls:CardView` |
| 栈布局 | `VerticalStackLayout` | `StackPanel` |
| 加载指示器 | `ActivityIndicator` | 自定义 `StackPanel` with emoji |
| WebView | `StockWebChartView` | `controls:StockWebChartView` |

#### 样式变更

**MAUI版本**:
```xml
<Style x:Key="StockCompactCardStyle" TargetType="Border">
    <Setter Property="Background" Value="{StaticResource CardGradientBrush}" />
    <Setter Property="StrokeShape" Value="RoundRectangle 8" />
    <Setter Property="Shadow">
        <Setter.Value>
            <Shadow Brush="{AppThemeBinding Light={StaticResource ShadowLight}, Dark={StaticResource ShadowDark}}" 
                    Offset="0,2" 
                    Radius="4" 
                    Opacity="0.1" />
        </Setter.Value>
    </Setter>
</Style>
```

**Avalonia版本**:
```xml
<Style Selector="Button.period-button">
    <Setter Property="FontSize" Value="12"/>
    <Setter Property="FontWeight" Value="Bold"/>
    <Setter Property="BorderBrush" Value="{DynamicResource SystemAccentColor}"/>
    <Setter Property="Background" Value="Transparent"/>
</Style>

<Style Selector="Button.period-button.selected">
    <Setter Property="Background" Value="{DynamicResource SystemAccentColor}"/>
    <Setter Property="Foreground" Value="White"/>
</Style>
```

**变更说明**:
- ✅ 使用 `CardView` 统一卡片样式
- ✅ 时间周期按钮使用 CSS-like 选择器样式
- ✅ 使用 `.selected` 类表示选中状态
- ✅ 动态资源 `DynamicResource` 支持主题切换

---

### 3. 转换器 (`PriceChangeColorConverter`)

**MAUI版本**:
```csharp
public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
{
    if (value is decimal change)
    {
        return change > 0 
            ? Application.Current.Resources["UpColor"] 
            : (change < 0 ? Application.Current.Resources["DownColor"] : Application.Current.Resources["NeutralColor"]);
    }
    return Colors.Gray;
}
```

**Avalonia版本**:
```csharp
public object Convert(IList<object?> values, Type targetType, object parameter, CultureInfo culture)
{
    if (values[0] is decimal change)
    {
        return change > 0 
            ? Brushes.Green 
            : (change < 0 ? Brushes.Red : Brushes.Gray);
    }
    return Brushes.Gray;
}
```

**变更说明**:
- ✅ Avalonia使用 `IMultiValueConverter` 接口
- ✅ 返回类型从 `Color` 改为 `IBrush`
- ✅ 资源访问方式改变

---

### 4. 数据加载优化

#### 异常处理

**之前（MAUI）**:
```csharp
private async Task LoadStockDataAsync(string stockCode)
{
    await SafeExecuteAsync(async () =>
    {
        var kLineDataSet = await _stockKLineService.GetDailyKLineDataAsync(stockCode);
        KLineDataSet = kLineDataSet;
    }, $"加载股票 {stockCode} 的K线数据");
}
```

**现在（Avalonia）**:
```csharp
private async Task LoadStockDataAsync(string stockCode)
{
    await SafeExecuteAsync(async () =>
    {
        HasError = false;
        ErrorMessage = string.Empty;

        var kLineDataSet = await _stockKLineService.GetDailyKLineDataAsync(stockCode);
        
        KLineDataSet = kLineDataSet;
        KLineData = new ObservableCollection<StockKLineData>(kLineDataSet.Data);
        
        // 如果StockName为空，使用股票代码
        if (string.IsNullOrEmpty(StockName))
        {
            StockName = stockCode;
        }

        // 计算价格信息
        CalculatePriceInfo(kLineDataSet.Data);

    }, $"加载股票 {stockCode} 的K线数据");
}
```

**优化说明**:
- ✅ 在加载前清除错误状态
- ✅ 自动设置股票名称
- ✅ 完整的价格信息计算

---

## 🎨 UI优化

### 1. 卡片化设计
- 使用 `CardView` 统一所有区域为卡片样式
- 更好的视觉层次和分组

### 2. 按钮状态优化
```xml
<Button Classes="period-button"
        Classes.selected="{Binding IsDailySelected}"
        Command="{Binding ChangeKLineTypeCommand}" 
        CommandParameter="daily" />
```
- 使用 `Classes.selected` 动态绑定选中状态
- 更清晰的视觉反馈

### 3. 错误提示优化
```xml
<StackPanel HorizontalAlignment="Center" 
            VerticalAlignment="Center" 
            Spacing="12">
    <TextBlock Text="⚠️" FontSize="48" />
    <TextBlock Text="数据加载失败" FontWeight="Bold"/>
    <TextBlock Text="{Binding ErrorMessage}" TextWrapping="Wrap" />
    <Button Content="🔄 重新加载" Command="{Binding RefreshDataCommand}" />
</StackPanel>
```
- 使用Emoji图标增强视觉效果
- 友好的错误消息展示
- 一键重试按钮

### 4. 加载状态优化
```xml
<StackPanel HorizontalAlignment="Center" 
            VerticalAlignment="Center"
            IsVisible="{Binding IsBusy}">
    <TextBlock Text="⏳" FontSize="48"/>
    <TextBlock Text="正在加载K线数据..." />
</StackPanel>
```
- 使用沙漏Emoji代替ActivityIndicator
- 更轻量级的实现

---

## 📊 组件依赖

### 核心依赖
1. **`StockWebChartView`** - 自定义WebView控件用于显示K线图
2. **`CardView`** - 统一的卡片容器组件
3. **`PriceChangeColorConverter`** - 价格变化颜色转换器
4. **`NavigationMessage`** - 导航消息类

### 服务依赖
1. **`StockKLineService`** - K线数据服务
2. **`WeakReferenceMessenger`** - 消息传递机制

---

## 🚀 使用方法

### 1. 在主窗口中注册导航

```csharp
public void Receive(NavigationMessage message)
{
    switch (message.PageName)
    {
        case "Stock":
            var viewModel = new StockPageViewModel(_logger, _stockKLineService);
            if (message.Parameter is Dictionary<string, object> parameters 
                && parameters.TryGetValue("code", out var code))
            {
                viewModel.SetStockCode(code.ToString()!);
            }
            CurrentPage = viewModel;
            break;
    }
}
```

### 2. 从其他页面导航到股票详情页

```csharp
WeakReferenceMessenger.Default.Send(new NavigationMessage("Stock", 
    new Dictionary<string, object> { { "code", "600000" } }
));
```

---

## ⚠️ 已知限制

1. **WebView集成**
   - 当前 `StockWebChartView` 使用模拟导航
   - 需要确认 `WebView.Avalonia.Desktop` 的正确API
   - JavaScript交互功能待完善

2. **股票名称获取**
   - `StockKLineDataSet` 不包含股票名称
   - 当前使用股票代码作为fallback
   - 可能需要额外的API调用获取股票名称

---

## 📝 测试清单

- [x] ViewModel编译通过
- [x] View编译通过
- [ ] 页面导航测试
- [ ] K线图表显示测试
- [ ] 时间周期切换测试
- [ ] 错误状态显示测试
- [ ] AI分析导航测试
- [ ] 数据刷新测试

---

## 🎯 后续优化建议

1. **完善WebView集成**
   - 确认正确的WebView API
   - 实现JavaScript交互
   - 完善图表数据更新机制

2. **添加更多信息**
   - 显示更多股票基本信息（市盈率、市值等）
   - 添加技术指标显示（MA、MACD等）
   - 支持多个股票对比

3. **性能优化**
   - 实现数据缓存
   - 优化K线数据加载
   - 添加数据预加载

4. **UI增强**
   - 添加图表缩放和平移
   - 支持暗黑模式
   - 增加动画效果

---

## 📚 相关文件

- `StockPageViewModel.cs` - 股票详情页ViewModel
- `StockPageView.axaml` - 股票详情页View
- `StockPageView.axaml.cs` - 股票详情页Code-behind
- `StockWebChartView.cs` - 自定义K线图表控件
- `PriceChangeColorConverter.cs` - 价格变化颜色转换器
