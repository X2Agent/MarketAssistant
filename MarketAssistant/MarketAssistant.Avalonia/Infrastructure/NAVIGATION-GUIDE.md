# Avalonia 导航指南

## 当前项目的导航实现

### 架构选择

项目采用 **简单 ViewModel 切换 + 消息机制** 的导航方式，适合以下场景：
- ✅ 简单的页面切换
- ✅ 左侧导航栏 + 内容区布局
- ✅ 不需要复杂的导航历史栈
- ✅ 不需要频繁的前进/后退操作

### 实现方式

#### 1. 消息定义 (`NavigationMessage.cs`)

```csharp
public class NavigationMessage
{
    public string PageName { get; }
    public object? Parameter { get; }  // 支持参数传递
    
    public NavigationMessage(string pageName, object? parameter = null)
    {
        PageName = pageName;
        Parameter = parameter;
    }
}
```

#### 2. 主窗口 ViewModel (`MainWindowViewModel.cs`)

```csharp
public partial class MainWindowViewModel : ViewModelBase, IRecipient<NavigationMessage>
{
    [ObservableProperty]
    private ViewModelBase? _currentPage;
    
    public MainWindowViewModel()
    {
        // 注册导航消息监听
        WeakReferenceMessenger.Default.Register(this);
    }
    
    public void Receive(NavigationMessage message)
    {
        // 根据页面名称创建对应的 ViewModel
        CurrentPage = message.PageName switch
        {
            "MCPConfig" => new MCPConfigPageViewModel(),
            "StockDetail" => new StockDetailViewModel(message.Parameter),
            _ => CurrentPage
        };
    }
}
```

#### 3. 发起导航

```csharp
// 简单导航
WeakReferenceMessenger.Default.Send(new NavigationMessage("MCPConfig"));

// 带参数导航
WeakReferenceMessenger.Default.Send(
    new NavigationMessage("StockDetail", stockCode)
);
```

### 优点

1. **简单易用** - 不需要学习复杂的路由框架
2. **解耦良好** - 页面间通过消息通信
3. **轻量级** - 基于已有的 CommunityToolkit.Mvvm
4. **适合 MVVM** - 符合 MVVM 模式

### 缺点与限制

1. **无导航历史** - 不支持浏览器式的前进/后退
2. **无 URL 路由** - 不像 Web 应用那样有 URL 路径
3. **生命周期简单** - 需要手动管理页面生命周期

---

## Avalonia 官方推荐方式对比

### 方式 1: ReactiveUI Router（官方推荐）

**适用场景：**
- 需要导航历史栈
- 需要前进/后退功能
- 需要深层嵌套导航
- Web 式的导航体验

**示例：**

```csharp
// ViewModel
public class MainWindowViewModel : ReactiveObject, IScreen
{
    public RoutingState Router { get; } = new RoutingState();
    
    public MainWindowViewModel()
    {
        // 导航到初始页面
        Router.Navigate.Execute(new HomeViewModel(this));
    }
}

public class HomeViewModel : ReactiveObject, IRoutableViewModel
{
    public string UrlPathSegment => "home";
    public IScreen HostScreen { get; }
    
    public ReactiveCommand<Unit, IRoutableViewModel> GoToDetail { get; }
    
    public HomeViewModel(IScreen screen)
    {
        HostScreen = screen;
        GoToDetail = ReactiveCommand.CreateFromObservable(
            () => HostScreen.Router.Navigate.Execute(new DetailViewModel(this.HostScreen))
        );
    }
}
```

**XAML：**

```xml
<rxui:RoutedViewHost Router="{Binding Router}" />
```

**优点：**
- ✅ 完整的导航栈
- ✅ 支持前进/后退
- ✅ 官方支持和文档
- ✅ 与 ReactiveUI 深度集成

**缺点：**
- ❌ 需要学习 ReactiveUI
- ❌ 额外的依赖
- ❌ 对简单场景过于复杂

---

### 方式 2: 自定义导航服务

**适用场景：**
- 需要更多控制
- 需要导航历史但不想用 ReactiveUI
- 需要自定义导航逻辑

**示例：**

```csharp
public interface INavigationService
{
    ViewModelBase? CurrentPage { get; }
    void NavigateTo(string pageName, object? parameter = null);
    void GoBack();
    bool CanGoBack { get; }
}

public class NavigationService : ObservableObject, INavigationService
{
    private readonly Stack<ViewModelBase> _navigationStack = new();
    private ViewModelBase? _currentPage;
    
    public ViewModelBase? CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }
    
    public bool CanGoBack => _navigationStack.Count > 1;
    
    public void NavigateTo(string pageName, object? parameter = null)
    {
        var page = CreatePage(pageName, parameter);
        if (page != null)
        {
            _navigationStack.Push(page);
            CurrentPage = page;
        }
    }
    
    public void GoBack()
    {
        if (CanGoBack)
        {
            _navigationStack.Pop();
            CurrentPage = _navigationStack.Peek();
        }
    }
    
    private ViewModelBase? CreatePage(string pageName, object? parameter)
    {
        return pageName switch
        {
            "Home" => new HomeViewModel(),
            "Detail" => new DetailViewModel(parameter),
            _ => null
        };
    }
}
```

---

## 最佳实践建议

### 对于当前项目（MarketAssistant）

**建议保持当前方式**，因为：

1. ✅ 应用结构简单（左侧导航 + 内容区）
2. ✅ 不需要复杂的导航历史
3. ✅ 消息机制已经足够解耦
4. ✅ 易于维护和理解

**可选改进：**

```csharp
// 1. 添加导航参数支持 ✅ 已完成
new NavigationMessage("MCPConfig", configId)

// 2. 添加导航拦截器（可选）
public class NavigationInterceptor
{
    public bool CanNavigate(string from, string to) 
    {
        // 检查用户权限、未保存的更改等
        return true;
    }
}

// 3. 添加页面生命周期（可选）
public interface INavigable
{
    Task OnNavigatedTo(object? parameter);
    Task OnNavigatedFrom();
}
```

---

## 何时应该切换到 ReactiveUI Router？

考虑切换的场景：

1. **需要浏览器式导航** - 前进/后退按钮
2. **多层级导航** - 例如：首页 → 分类 → 商品 → 详情
3. **深度链接支持** - 通过 URL 直接打开特定页面
4. **复杂的导航状态管理** - 需要保存和恢复导航状态

---

## 参考资源

- [Avalonia 官方文档 - View Models](https://docs.avaloniaui.net/docs/concepts/the-mvvm-pattern/view-models)
- [ReactiveUI Router 文档](https://www.reactiveui.net/docs/handbook/routing/)
- [CommunityToolkit.Mvvm Messenger](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/messenger)

---

## 总结

**当前实现评分：**
- 简洁性：⭐⭐⭐⭐⭐
- 可维护性：⭐⭐⭐⭐
- 功能完整性：⭐⭐⭐
- 性能：⭐⭐⭐⭐⭐
- 学习曲线：⭐⭐⭐⭐⭐

**结论：** 对于当前项目，实现方式是 **合理且符合最佳实践** 的，无需立即更改。
