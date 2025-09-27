# MAUI到Avalonia迁移指南

本文档提供了将MarketAssistant应用程序从.NET MAUI迁移到Avalonia UI的综合指南。

## 目录
1. [概述](#概述)
2. [架构差异](#架构差异)
3. [项目结构迁移](#项目结构迁移)
4. [UI XAML迁移](#ui-xaml迁移)
5. [导航系统迁移](#导航系统迁移)
6. [ViewModel和数据绑定](#viewmodel和数据绑定)
7. [包依赖迁移](#包依赖迁移)
8. [资源和资产迁移](#资源和资产迁移)
9. [分步迁移过程](#分步迁移过程)
10. [测试和验证](#测试和验证)

## 概述

MarketAssistant应用程序当前使用.NET MAUI构建，支持跨平台移动和桌面应用程序。Avalonia UI是一个基于XAML的跨平台UI框架，用于创建现代化、高性能的桌面应用程序。本迁移指南概述了从MAUI迁移到Avalonia所需的必要更改。

主要差异包括：
- UI框架从MAUI到Avalonia
- 导航从基于Shell到基于窗口/内容
- 不同的XAML语法和控件
- 不同的资源管理

## 架构差异

### MAUI架构
- 基于原生平台控件
- 基于Shell的导航系统
- Application, AppShell, ContentPage结构
- 平台特定实现

### Avalonia架构
- 跨平台渲染引擎
- 基于窗口的导航系统
- Application, Window, UserControl结构
- 跨平台一致行为

## 项目结构迁移

### MAUI项目结构
```
MarketAssistant/
├── App.xaml/App.xaml.cs
├── AppShell.xaml/AppShell.xaml.cs
├── MainPage.xaml/MainPage.xaml.cs
├── Pages/
├── Controls/
├── ViewModels/
├── Models/
├── Resources/
│   ├── Images/
│   ├── Styles/
│   └── Raw/
└── Services/
```

### Avalonia项目结构
```
MarketAssistant.Avalonia/
├── App.axaml/App.axaml.cs
├── MainWindow.axaml/MainWindow.axaml.cs
├── Views/
├── ViewModels/
├── Models/
├── Controls/
├── Converters/
├── Assets/
└── Services/
```

### 项目结构迁移步骤
1. 将ViewModel从MAUI项目移动到Avalonia项目
2. 将Models从MAUI项目移动到Avalonia项目
3. 将Services从MAUI项目移动到Avalonia项目
4. 将Pages转换为Avalonia中的Views
5. 将Controls转换为Avalonia兼容控件
6. 迁移Resources到Assets（用于图像）并创建适当的样式

## UI XAML迁移

### 主要语法差异

#### MAUI XAML
```xml
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Class="MarketAssistant.Pages.HomePage">
    <Grid>
        <CollectionView ItemsSource="{Binding Items}" />
    </Grid>
</ContentPage>
```

#### Avalonia XAML
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="MarketAssistant.Avalonia.Views.HomeView">
    <Grid>
        <ItemsControl ItemsSource="{Binding Items}" />
    </Grid>
</UserControl>
```

### 控件映射

| MAUI控件 | Avalonia等效控件 | 说明 |
|-------------|-------------------|-------|
| ContentPage | UserControl或Window | 页面变成UserControl，主页变成Window |
| Shell | 主窗口+导航 | Shell导航变成自定义导航 |
| CollectionView | ItemsControl或DataGrid | CollectionView没有直接等效项 |
| ContentView | UserControl | 都是内容容器 |
| Frame | Border | Frame样式可以用Border复制 |
| Grid, StackLayout | Grid, StackPanel | 功能相似，名称相同 |
| SearchBar | TextBox配合按钮 | 需要自定义实现 |
| ImageButton | Image配合Button | Avalonia中没有ImageButton |
| Border | Border | 功能类似但语法略有不同 |

### 需要替换的MAUI特定功能

1. **CollectionView**: 用ItemsControl、ListBox或DataGrid替换，根据需求而定
2. **Shell导航**: 用ReactiveUI或类似模式自定义导航替换
3. **AppThemeBinding**: 用Avalonia的ThemeVariant系统替换
4. **VisualStateManager**: 用Avalonia的样式和触发器替换
5. **GestureRecognizers**: 用Avalonia的输入事件替换
6. **BindableLayout**: 用ItemsControl或WrapPanel替换
7. **TapGestureRecognizer**: 用DoubleTapped或PointerPressed事件替换

### 第三阶段：UI实现
8. **将MAUI XAML转换为Avalonia XAML** - ✅ 部分完成 (HomePage、StockView、AboutView、SettingView和FavoritesView已迁移)
9. **实现自定义控件**以替换MAUI特定控件 - ❌ 待完成
10. **使用Avalonia主题设计应用程序** - ❌ 待完成

## 导航系统迁移

### MAUI导航
- 基于Shell的导航，带有Flyout
- 使用Shell.Current.GoToAsync()的基于路由的导航
- 内置导航堆栈管理

### Avalonia导航
- 基于窗口或UserControl的导航
- 需要自定义导航实现
- 通常使用ReactiveUI等MVVM框架或自定义导航服务

### 迁移方法

1. **用自定义导航替换Shell导航**:
   - 创建带有导航控件的主窗口
   - 实现导航服务以管理视图切换
   - 用菜单或自定义控件复制flyout功能

2. **更新导航逻辑**:
   - 用自定义导航替换Shell导航调用
   - 更新ViewModel导航命令
   - 以不同方式处理参数传递

### 导航迁移示例

#### MAUI导航
```csharp
// 在ViewModel中
await Shell.Current.GoToAsync("stock", new Dictionary<string, object>
{
    { "code", stockCode }
});
```

#### Avalonia导航
```csharp
// 在ViewModel中（使用自定义导航服务）
_navigationService.NavigateTo<StockViewModel>(new Dictionary<string, object>
{
    { "code", stockCode }
});
```

## ViewModel和数据绑定

### 积极的迁移方面

1. **CommunityToolkit.Mvvm**: 在两个项目中都使用，因此ViewModel基类保持兼容
2. **数据绑定**: 核心绑定概念保持相似
3. **命令**: ICommand实现保持兼容

### 注意事项

1. **绑定语法**: MAUI和Avalonia之间可能存在一些语法差异
2. **验证**: 不同的验证系统可能需要更新
3. **可观察属性**: CommunityToolkit.Mvvm使这保持兼容

### ViewModel迁移示例

#### MAUI ViewModel
```csharp
public class HomeViewModel : BaseViewModel // MAUI特定基类
{
    public ICommand NavigateToStockCommand { get; set; }
    
    public HomeViewModel(/* MAUI特定服务 */)
    {
        NavigateToStockCommand = new Command<StockItem>(OnNavigateToStock);
    }
}
```

#### Avalonia ViewModel
```csharp
public class HomeViewModel : ViewModelBase // Avalonia/CommunityToolkit基类
{
    public ReactiveCommand<Unit, Unit> NavigateToStockCommand { get; set; }
    
    public HomeViewModel(/* Avalonia兼容服务 */)
    {
        NavigateToStockCommand = ReactiveCommand.Create<StockItem>(OnNavigateToStock);
    }
}
```

## 包依赖迁移

### MAUI依赖
- Microsoft.Maui.Controls
- Microsoft.Maui.Graphics
- CommunityToolkit.Maui
- Microsoft.Extensions.*包

### Avalonia依赖
- Avalonia
- Avalonia.Desktop
- Avalonia.Themes.Fluent
- Avalonia.Fonts.Inter
- CommunityToolkit.Mvvm (已存在)

### 依赖映射

| MAUI包 | Avalonia等效包 | 状态 |
|-------------|-------------------|--------|
| Microsoft.Maui.Controls | Avalonia | 需要迁移 |
| CommunityToolkit.Maui | CommunityToolkit.Mvvm | 部分兼容 |
| Microsoft.Maui.Graphics | Avalonia.Media | 不同API |

### MarketAssistant中的特定依赖

#### 保持（兼容）
- CommunityToolkit.Mvvm
- Microsoft.Extensions.Caching.Memory
- DotLiquid
- DocumentFormat.OpenXml
- Microsoft.Playwright
- Microsoft.SemanticKernel*包
- Serilog*包
- PdfPig
- SmartReader
- System.Linq.Async
- Microsoft.ML.OnnxRuntime
- System.Numerics.Tensors
- SkiaSharp

#### 适配（Avalonia替代品）
- Microsoft.Maui.Controls → Avalonia
- Microsoft.Maui.Graphics.Text.Markdig → Avalonia.Markup

#### 移除（MAUI特定）
- Microsoft.Maui.* (所有包)

## 资源和资产迁移

### 图像
- MAUI: Resources/Images/
- Avalonia: Assets/

### 字体
- MAUI: Resources/Fonts/
- Avalonia: Assets/Fonts/或使用Avalonia.Fonts.Inter包

### 样式
- MAUI: Resources/Styles/
- Avalonia: App.axaml或单独的资源字典

### 迁移步骤：
1. 将图像从MAUI Resources/Images移动到Avalonia Assets
2. 更新XAML中的图像路径（Avalonia中不需要路径前缀）
3. 将MAUI样式转换为Avalonia样式
4. 更新任何资源字典引用
5. 替换MAUI特定颜色/资源与Avalonia等效项

## 分步迁移过程

### 第一阶段：准备
1. **分析MAUI项目结构** - ✅ 已完成
2. **设置Avalonia项目基线** - ✅ 项目已存在
3. **迁移模型和服务** - ✅ 业务逻辑已迁移

### 第二阶段：核心迁移
4. **从MAUI项目迁移ViewModels**到Avalonia - ✅ 已完成
5. **基于MAUI页面创建Avalonia特定视图**
6. **实现导航系统**以替换Shell导航
7. **添加Avalonia特定转换器**

### 第三阶段：UI实现
8. **将MAUI XAML转换为Avalonia XAML**
9. **实现自定义控件**以替换MAUI特定控件
10. **使用Avalonia主题设计应用程序**

### 第四阶段：集成和测试
11. **连接ViewModels到视图**
12. **测试导航功能**
13. **验证所有功能正常工作**
14. **性能优化**

## 测试和验证

### 单元测试
- ViewModels应保持相同功能，更改最小
- Services和Models中的业务逻辑应保持不变
- 导航逻辑需要使用新系统进行测试

### UI测试
- 验证所有屏幕正确渲染
- 测试所有导航路径
- 确认绑定按预期工作
- 确保样式符合原始设计意图

### 平台特定测试
- Windows: 首先测试主要平台
- macOS: Windows工作后在macOS上测试
- Linux: 最终平台测试

## 潜在挑战和解决方案

### 挑战：复杂的CollectionView实现
**解决方案**: 使用DataGrid或创建自定义ItemsControl配合适当模板

### 挑战：Shell导航替换
**解决方案**: 使用MVVM模式实现自定义导航，配合菜单/侧边面板

### 挑战：MAUI特定控件
**解决方案**: 创建Avalonia等效控件或使用可用替代品

### 挑战：主题/样式差异
**解决方案**: 使用类似颜色方案和设计模式将MAUI样式移植到Avalonia

## 推荐迁移顺序

1. **服务和模型**: 首先迁移，因为它们与平台无关 - ✅ 已完成
2. **ViewModels**: 使用CommunityToolkit.Mvvm进行最小更改迁移 - ✅ 已完成
3. **主窗口**: 设置主应用程序窗口 - ❌ 待完成
4. **导航系统**: 实现视图间的导航 - ❌ 待完成
5. **Home视图**: 迁移主要用户界面 - ✅ 已完成
6. **其他视图**: Stock, Settings等页面 - ⏳ 部分完成
7. **附加控件**: 自定义控件如CardView - ❌ 待完成
8. **资产**: 图像、图标和样式 - ❌ 待完成
9. **测试**: 在整个过程中验证功能 - ❌ 待完成

## 结论

从MAUI迁移到Avalonia涉及大量的UI和导航更改，而业务逻辑基本保持不变。CommunityToolkit.Mvvm的兼容性有助于保持ViewModel的一致性。我们已经成功迁移了大部分后台逻辑（服务、模型和ViewModels），并开始迁移UI层（已迁移HomePage和StockView）。

迁移工作正按计划进行：
- 核心业务逻辑已迁移
- ViewModel层已适配Avalonia
- 部分UI页面已转换为Avalonia XAML

下一步需要继续迁移剩余的UI页面，实现Avalonia导航系统，以及创建Avalonia等效的自定义控件。完整的迁移将在所有页面、导航和UI控件都迁移到Avalonia后完成。