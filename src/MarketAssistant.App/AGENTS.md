# MarketAssistant.App — AGENTS.md

主应用入口项目（Avalonia 桌面应用）。**职责限定为 UI 宿主**：包含 View、ViewModel、导航、通知、对话框、样式资源和内容文件输出；业务逻辑、Agent 实现、Workflow、Tool、MCP、交易引擎收敛在 `MarketAssistant.App.Services`，RAG 基础能力收敛在 `MarketAssistant.Rag`。

---

## 目录结构

```
MarketAssistant.App/
├── config/
│   └── prompts/                   ← 分析师提示词配置（每个分析师一个 yaml 文件）
│       ├── FundamentalAnalyst.yaml
│       ├── FinancialAnalyst.yaml
│       ├── TechnicalAnalyst.yaml
│       ├── MarketSentimentAnalyst.yaml
│       ├── NewsEventAnalyst.yaml
│       ├── CryptoMetricsAnalyst.yaml   ← Crypto 专属指标分析师
│       ├── CoordinatorAnalyst.yaml
│       └── Crypto/                     ← Crypto 市场差异化提示词
├── Converts/                      ← AXAML 值转换器
├── Infrastructure/                ← ViewLocator 等 UI 宿主基础设施
├── Resources/Styles/              ← Avalonia 样式资源字典
├── Services/                      ← UI 适配服务
│   ├── Dialog/                    ← Avalonia 对话框实现
│   ├── Navigation/                ← 页面导航
│   └── Notification/              ← UI 通知
├── ViewModels/                    ← MVVM ViewModel
├── Views/                         ← Avalonia AXAML 视图
│   ├── Controls/                  ← 自定义控件
│   ├── Components/                ← 复合组件
│   ├── Pages/                     ← 页面视图
│   └── Windows/                   ← 窗口
├── skills/                        ← 输出到运行目录的 Skill/参考资源
└── Assets/                        ← 图片、图标
```

---

## UI 与样式约定（Avalonia AXAML）

- **文件格式**：视图文件使用 `.axaml` 扩展名。
- **布局约束**：
  - `Padding`/`Margin`/间距使用 **4 的倍数**（4/8/12/16），原则上不超过 16。
  - **禁止硬编码数值**：使用 `Resources/Styles/Spacing.axaml` 中的资源（如 `{StaticResource SmallMargin}`），或在 `UserControl.Resources` 中定义局部资源。
- **样式管理**：统一遵循 `Resources/Styles/` 中的集中式样式资源，避免硬编码颜色与字体。
- **资源引用**：
  - `Colors.axaml` 中 `Primary`、`PrimaryLight` 等是 **Color** 类型，`Background`/`Foreground` 必须使用对应 **Brush**（`PrimaryBrush`、`PrimaryLightBrush`）。
  - 主题感知资源 → `{DynamicResource}`；固定资源 → `{StaticResource}`。
- **控件**：优先使用 Avalonia 内置控件。
- **资产**：非必要不改动图片与资产文件。

### 加载态与状态浮层（强制约定）

页面/区块的加载、空、失败三态必须走统一机制，禁止每页自行手写提示面板。

- **状态源**：`ViewModelBase` 提供 `State`（`LoadState`）、`IsInitialLoading`、`OverlayState`、`OverlayMessage`、`ErrorMessage`、`EmptyMessage`、`RetryCommand`。
  - 加载方法一律通过 `protected Task LoadAsync(Func<CancellationToken, Task<bool>> loader, string operationName, ...)` 编写：loader 返回 `true` → `Ready`，`false` → `Empty`，抛 `OperationCanceledException` → `Idle`，其他异常 → `Failed`（经 `ErrorMessageMapper` 转可读文案）。
  - 构造函数末尾用 `SetRetryCommand(() => LoadXxxAsync())` 注册重试入口，再 `_ = LoadXxxAsync();` 触发首屏加载。
- **状态浮层**：统一使用 `Views/Controls/StateOverlayView`，绑定 `OverlayState` / `OverlayMessage` / `RetryCommand`。该控件**自管理 `IsVisible`**（加载/空/失败时可见），页面无法从 XAML 外部关闭它——只能通过改变绑定的 `State` 来控制。
  - `OverlayState` 而非 `State`：已有内容时刷新不显示浮层，避免内容闪烁。
  - 重试按钮仅在 `Failed` 且 `RetryCommand` 非空时出现（`CanRetry`），空态不会误显示"重试"。
- **骨架屏优先于转圈**：首屏加载展示骨架屏（绑定 `IsInitialLoading`），同时把内容区反相门控为 `{Binding !IsInitialLoading}`。骨架屏存在时必须给 `StateOverlayView` 加 `ShowLoading="False"`，否则浮层的加载点阵会叠在骨架上。
- **不要把长耗时 AI 操作接入该机制**：分析进度蒙层（如 `AssetSelectionPageView` 的 `ZIndex=1000` 分析中蒙层、`AgentAnalysisPageView` 的 `ProgressDisplayView`）表达的是"任务进行中"而非"数据加载中"，继续使用 `IsBusy` + `SafeExecuteAsync`。

### 骨架样式

骨架屏统一使用 `Resources/Styles/SkeletonStyles.axaml` 的类：

| 类 | 语义 |
|---|---|
| `Border.skeleton` | 基础类，带呼吸动画，必须搭配下方修饰类 |
| `skeleton-text` | 文本行（H12） |
| `skeleton-title` | 标题行（H18） |
| `skeleton-row` | 列表行（H44） |
| `skeleton-card` | 卡片（MinHeight 96） |
| `skeleton-panel` | 面板（MinHeight 240） |
| `skeleton-circle` | 头像/图标（32×32） |

- 骨架尺寸按真实内容形态用显式 `Height`/`Margin` 覆盖，行数与真实列表首屏可见条数一致，避免加载完成时的布局跳动。
- 骨架只服务**首屏**（`IsInitialLoading`）；刷新（`IsLoading` 且已有内容）不显示骨架。

---

## ViewModel 编码规范

- 使用 `CommunityToolkit.Mvvm` 源生成器：`[ObservableProperty]`、`[RelayCommand]`。
- `ICommand` 属性禁止在 getter 中每次创建新实例。
- 订阅外部事件的 ViewModel 实现 `IDisposable`，在 `Dispose` 中取消订阅。
- `ObservableCollection` 在构造函数中初始化一次。
- 异常处理：使用 `SafeExecuteAsync` 或 `ErrorMessageMapper`，**不要吞并异常**。
- 通过 DI 容器获取依赖，不使用 `ServiceLocator`。
- ViewModel 可以消费 `MarketAssistant.App.Services` 暴露的业务服务，但不要在 UI 层新增 Agent、Tool、Workflow 实现。
- 列表页筛选逻辑（纯函数、无 IO）抽为独立静态类（如 `AlertHistoryFilter`），ViewModel 只做装配与状态映射，便于脱离 Avalonia 单测。

### 告警历史页（PriceAlertPage → 告警历史）

- `AlertHistoryViewModel` 持有 `_allAlerts` 全量副本，可见集 `Alerts` 由 `AlertHistoryFilter.Matches` 按级别/标的关键词重建；筛选后为空与"无告警记录"是两种空态，文案必须区分（`SetEmpty` 传不同消息，不要复用同一句）。
- 历史告警的"AI 归因"按钮经 `NavigationMessage("Analysis", ...)` 跳转多分析师研判页；告警归属市场与当前市场不一致时先 `MarketContext.SwitchMarket`，否则分析链路会解析错市场的 Keyed 数据服务。系统级告警（无标的）不展示该按钮（`AlertEvent.HasSymbol` 门控）。
- `DataTemplate` 内的按钮命令经 `$parent[ItemsControl].((vm:XxxViewModel)DataContext).Command` 绑定到宿主 VM（`x:DataType` 不可跨 DataTemplate 冒泡）。

---

## 边界约定

- `MarketAssistant.App` 不再承载 Agent Tool、Workflow、RAG、交易引擎等运行时代码。
- 新增 Agent/Tool/Workflow/业务服务时，放到 `MarketAssistant.App.Services`；新增文档解析、向量化、检索、重排等 RAG 基础能力时，放到 `MarketAssistant.Rag`；本项目只保留 UI 相关适配。
- ViewModel 禁止直接注入 `MarketAssistant.DataProviders` 的具体实现类（如 `BinanceMarketDataService`、`BinanceWebSocketService`）；行情/实时数据应经 `MarketAssistant.App.Services` 编排层暴露的抽象访问，避免多市场抽象泄漏到 UI 层。
- `skills/` 作为内容文件随 App 输出，由运行时从输出目录加载；不要在 UI 层复制第二套 Skill 加载逻辑。
- 版本号定义在 `.csproj` 的 `<Version>` 属性中，运行时通过 `AppInfo.Version` 获取。

---

## DI 注册

- 入口：`Program.ConfigureServices()`。
- UI 注册入口：`Services/ServiceCollectionExtensions.cs` 中的 `AddApplicationServices()` 和 `AddViewModels()`。
- `AddApplicationServices()` 会先调用 `AddBusinessServices()`，后者定义在 `MarketAssistant.App.Services/Services/ServiceCollectionExtensions.cs`。
- 新增 UI 服务在本项目注册；新增业务/运行时服务在 `MarketAssistant.App.Services` 注册。

---

## 安全

- API 密钥（Binance、AI 模型）通过 `IUserSettingService` 管理，持久化在用户本地目录，**禁止提交到仓库**。
- 涉及 Binance 等交易所签名逻辑的代码修改时，需确保 HMAC 密钥不被日志记录。

---

## 配置文件

- `config/prompts/*.yaml`：分析师提示词配置，每个分析师一个独立文件（如 `FundamentalAnalyst.yaml`），含 `name`、`displayName`、`temperature`、`topP`、`topK`、`instructions` 字段，运行时热加载。

---

## 构建与运行

```bash
# 构建
dotnet build src/MarketAssistant.App/MarketAssistant.App.csproj -c Debug

# 运行
dotnet run --project src/MarketAssistant.App/MarketAssistant.App.csproj -c Debug
```
