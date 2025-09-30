# ReactiveUI Router 集成示例（可选）

> ⚠️ **注意：** 这是一个可选的高级功能示例。对于当前项目，**不建议**引入，因为当前的消息机制已经足够好用。

---

## 为什么不建议引入？

当前项目的导航特点：
- ✅ 固定侧边栏布局（4 个主页面）
- ✅ 简单的二级导航（设置 → MCP配置）
- ✅ 不需要浏览器式的前进/后退
- ✅ 不需要复杂的导航历史栈

**引入 ReactiveUI Router 会带来：**
- ❌ 额外的学习成本
- ❌ 更多的代码复杂度
- ❌ 额外的 NuGet 包依赖
- ❌ 对简单场景来说是过度设计

---

## 如果你仍想使用（仅供参考）

### 1. 安装 ReactiveUI 包

```xml
<PackageReference Include="Avalonia.ReactiveUI" Version="11.0.0" />
```

### 2. 修改 MainWindowViewModel

```csharp
using ReactiveUI;
using System.Reactive;

public partial class MainWindowViewModel : ViewModelBase, IScreen
{
    // ReactiveUI Router（替代 CurrentPage）
    public RoutingState Router { get; } = new RoutingState();
    
    // 保留原有的导航项
    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }
    
    // 导航命令
    public ReactiveCommand<string, IRoutableViewModel> NavigateToPage { get; }
    
    public MainWindowViewModel()
    {
        NavigationItems = new ObservableCollection<NavigationItemViewModel>
        {
            new NavigationItemViewModel("首页", "...", "...", "Home"),
            new NavigationItemViewModel("收藏", "...", "...", "Favorites"),
            new NavigationItemViewModel("设置", "...", "...", "Settings"),
            new NavigationItemViewModel("关于", "...", "...", "About")
        };
        
        // 创建导航命令
        NavigateToPage = ReactiveCommand.CreateFromObservable<string, IRoutableViewModel>(
            pageName => Router.Navigate.Execute(CreatePage(pageName))
        );
        
        // 默认导航到首页
        Router.Navigate.Execute(new HomePageViewModel(this));
    }
    
    private IRoutableViewModel CreatePage(string pageName)
    {
        return pageName switch
        {
            "Home" => new HomePageViewModel(this),
            "Favorites" => new FavoritesPageViewModel(this),
            "Settings" => new SettingsPageViewModel(this),
            "About" => new AboutPageViewModel(this),
            "MCPConfig" => new MCPConfigPageViewModel(this),
            _ => throw new ArgumentException($"Unknown page: {pageName}")
        };
    }
}
```

### 3. 修改 ViewModel 实现 IRoutableViewModel

```csharp
using ReactiveUI;

public class HomePageViewModel : ViewModelBase, IRoutableViewModel
{
    public string UrlPathSegment => "home";
    public IScreen HostScreen { get; }
    
    public ReactiveCommand<Unit, IRoutableViewModel> GoToMCPConfig { get; }
    
    public HomePageViewModel(IScreen screen)
    {
        HostScreen = screen;
        
        // 创建导航命令
        GoToMCPConfig = ReactiveCommand.CreateFromObservable(
            () => HostScreen.Router.Navigate.Execute(new MCPConfigPageViewModel(screen))
        );
    }
}
```

### 4. 修改 MainWindow.axaml

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:rxui="http://reactiveui.net"
        ...>
    
    <Grid ColumnDefinitions="250,*">
        <!-- 左侧导航保持不变 -->
        <Border Grid.Column="0">
            <ListBox ItemsSource="{Binding NavigationItems}"
                     SelectedItem="{Binding SelectedNavigationItem}">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <!-- 导航项模板 -->
                        <Button Command="{Binding $parent[Window].((vm:MainWindowViewModel)DataContext).NavigateToPage}"
                                CommandParameter="{Binding PageName}">
                            <TextBlock Text="{Binding Title}"/>
                        </Button>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>
        </Border>
        
        <!-- 右侧内容区 - 使用 RoutedViewHost -->
        <rxui:RoutedViewHost Grid.Column="1" 
                             Router="{Binding Router}">
            <rxui:RoutedViewHost.ViewLocator>
                <local:ViewLocator />
            </rxui:RoutedViewHost.ViewLocator>
        </rxui:RoutedViewHost>
    </Grid>
</Window>
```

### 5. 创建 ViewLocator

```csharp
using ReactiveUI;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

public class ViewLocator : IViewLocator
{
    public IViewFor ResolveView<T>(T viewModel, string? contract = null)
    {
        var viewModelName = viewModel!.GetType().FullName!;
        var viewTypeName = viewModelName.Replace("ViewModel", "View");
        var viewType = Type.GetType(viewTypeName);
        
        if (viewType != null)
        {
            return (IViewFor)Activator.CreateInstance(viewType)!;
        }
        
        return new TextBlock { Text = $"Not Found: {viewModelName}" };
    }
}
```

---

## 对比：当前方式 vs ReactiveUI Router

### 当前方式（推荐）✅

```csharp
// 发送导航
WeakReferenceMessenger.Default.Send(new NavigationMessage("MCPConfig"));

// 接收导航
public void Receive(NavigationMessage message) 
{
    CurrentPage = new MCPConfigPageViewModel();
}
```

**代码量：** ~50 行
**复杂度：** ⭐
**学习成本：** ⭐

### ReactiveUI Router 方式

```csharp
// 创建 Router
public RoutingState Router { get; } = new RoutingState();

// 导航
Router.Navigate.Execute(new MCPConfigPageViewModel(this));

// 后退
Router.NavigateBack.Execute();
```

**代码量：** ~200+ 行
**复杂度：** ⭐⭐⭐⭐
**学习成本：** ⭐⭐⭐⭐

---

## 何时应该使用 ReactiveUI Router？

✅ **适合的场景：**
1. 需要浏览器式的前进/后退功能
2. 多层级嵌套导航（3 层以上）
3. 需要保存和恢复导航状态
4. 需要深度链接支持
5. 已经在使用 ReactiveUI 的其他功能

❌ **不适合的场景：**
1. 固定侧边栏导航（当前项目）
2. 简单的页面切换
3. 不需要历史栈
4. 团队不熟悉 ReactiveUI

---

## 性能对比

| 方式 | 内存占用 | 导航速度 | 启动时间 |
|---|---|---|---|
| **当前消息机制** | 低 | 极快 | 快 |
| **ReactiveUI Router** | 中 | 快 | 中 |

---

## 最终建议

### 对于 MarketAssistant 项目：

**保持当前的消息机制方式！** ✅

原因：
1. ✅ 简单高效
2. ✅ 易于维护
3. ✅ 符合项目需求
4. ✅ 团队容易理解
5. ✅ 性能更好

### 如果将来需要升级：

如果项目发展到需要以下功能时，再考虑迁移：
- 需要复杂的多层级导航
- 需要前进/后退按钮
- 需要保存导航历史
- 需要深度链接

---

## 总结

**MVVM 架构 + ReactiveUI Router = 完全兼容 ✅**

但对于你的项目来说：
- **当前方式** = 简单、够用、高效 ⭐⭐⭐⭐⭐
- **ReactiveUI Router** = 功能强大，但过度设计 ⭐⭐

**建议：继续使用当前的消息机制方式！** 🚀
