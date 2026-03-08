# MarketAssistant.App — AGENTS.md

主应用入口项目（Avalonia 桌面应用），引用所有其他子项目。包含 UI、业务逻辑、Agent 实现、交易引擎、RAG 等全部运行时代码。

---

## 目录结构

```
MarketAssistant.App/
├── Agents/                        ← AI Agent 实现
│   ├── MarketChatSession.cs       ← 聊天会话管理
│   ├── Analysts/                  ← 分析师实现（Technical/Fundamental/Financial/Sentiment/News/Coordinator）
│   ├── ContextProviders/          ← 上下文提供者
│   ├── InvestmentSelection/       ← 投资选择工作流（三步骤确定性 Workflow）
│   │   ├── Executors/             ← 步骤执行器
│   │   ├── Models/                ← 工作流模型
│   │   └── Strategies/            ← 市场特定策略（A 股 / Crypto）
│   ├── MarketAnalysis/            ← 市场分析工作流（Fan-Out/Fan-In 并发分析）
│   │   └── Executors/             ← 分发/聚合/协调执行器
│   ├── Tools/                     ← Agent 工具实现
│   │   ├── AShare/                ← A 股工具
│   │   ├── Crypto/                ← 加密货币工具
│   │   └── GroundingSearchTools.cs
│   └── Trading/                   ← 交易 Agent
├── Applications/                  ← 业务服务层
│   ├── Analysis/                  ← 分析编排服务
│   ├── AssetScreener/             ← 资产筛选
│   ├── Assets/                    ← 资产信息
│   ├── Cache/                     ← 资产缓存
│   ├── Charts/                    ← K 线数据
│   ├── Crypto/                    ← Binance 认证/账户
│   ├── Favorites/                 ← 收藏管理
│   ├── History/                   ← 浏览历史
│   ├── Home/                      ← 首页数据
│   ├── InvestmentSelection/       ← 投资选择业务
│   ├── News/                      ← 新闻快讯
│   ├── PriceAlert/                ← 价格提醒
│   ├── Settings/                  ← 设置与版本
│   └── Telegrams/                 ← 电报快讯
├── config/
│   ├── models.yaml                ← AI 模型与供应商配置
│   └── prompts/analysts.yaml      ← 分析师提示词配置
├── Converts/                      ← AXAML 值转换器
├── Infrastructure/
│   ├── Abstractions/              ← 服务接口
│   ├── AdaptiveCards/             ← 自适应卡片转换与解析
│   ├── Configuration/             ← 偏好设置
│   ├── Core/                      ← 文件系统、全局异常、ViewLocator
│   └── Factories/                 ← AnalystAgent/ChatClient/Embedding/TradingAgent 工厂
├── Rag/                           ← RAG（向量化、检索、重排、文档解析）
├── Resources/Styles/              ← Avalonia 样式资源字典
├── Services/                      ← 横切关注点服务
│   ├── Archive/                   ← 报告归档
│   ├── Browser/                   ← 浏览器 / Playwright
│   ├── Cache/                     ← 分析缓存
│   ├── Dialog/                    ← 对话框
│   ├── Export/                    ← Markdown 报告导出
│   ├── Market/                    ← MarketContext 与市场能力
│   ├── Mcp/                       ← MCP Server 集成
│   ├── Navigation/                ← 页面导航
│   ├── Notification/              ← 通知
│   └── Settings/                  ← 用户设置持久化
├── Trading/                       ← 交易引擎
│   ├── Exchanges/BinanceExchangeClient.cs
│   ├── TradeExecutor.cs, RiskManager.cs
│   ├── StrategyEngine.cs, MarketMonitor.cs
│   └── TradingDataService.cs
├── ViewModels/                    ← MVVM ViewModel
├── Views/                         ← Avalonia AXAML 视图
│   ├── Controls/                  ← 自定义控件
│   ├── Components/                ← 复合组件
│   ├── Pages/                     ← 页面视图
│   └── Windows/                   ← 窗口
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

---

## ViewModel 编码规范

- 使用 `CommunityToolkit.Mvvm` 源生成器：`[ObservableProperty]`、`[RelayCommand]`。
- `ICommand` 属性禁止在 getter 中每次创建新实例。
- 订阅外部事件的 ViewModel 实现 `IDisposable`，在 `Dispose` 中取消订阅。
- `ObservableCollection` 在构造函数中初始化一次。
- 异常处理：使用 `SafeExecuteAsync` 或 `ErrorMessageMapper`，**不要吞并异常**。
- 通过 DI 容器获取依赖，不使用 `ServiceLocator`。

---

## Agent 与工具约定

- **工具注册**：市场特定实现注册为 Keyed Service（key = `MarketType`），通用工具注册为 Singleton。
- **分析师工具声明**：通过 `[RequiresTools(typeof(IXxxTools))]` 声明，`AnalystAgentFactory` 自动解析。
- **ChatSession 上下文注入**：通过 `MarketChatSession.InjectAnalysisContext()` 注入，不手动操作 MAF `AgentSession` 内部历史。
- **ChatSession 工具范围**：仅持有 `GroundingSearchTools` + MCP 工具，市场数据工具在 Workflow 阶段由分析师使用。
- **版本号**：定义在 `.csproj` 的 `<Version>` 属性中，运行时通过 `AppInfo.Version` 获取。

---

## DI 注册

- 入口：`Program.ConfigureServices()` → 调用 `services.AddApplicationServices()`（`Services/ServiceCollectionExtensions.cs`）和 `services.AddRagServices()`（`Rag/Extensions/ServiceCollectionExtensions.cs`）。
- 新增服务在对应的 `ServiceCollectionExtensions` 中注册；市场特定实现使用 `AddKeyedSingleton<T>(MarketType)`。

---

## 安全

- API 密钥（Binance、AI 模型）通过 `IUserSettingService` 管理，持久化在用户本地目录，**禁止提交到仓库**。
- `Trading/Exchanges/BinanceExchangeClient.cs` 中的签名逻辑涉及 HMAC 密钥，修改时需确保密钥不被日志记录。

---

## 配置文件

- `config/prompts/analysts.yaml`：分析师提示词配置，每个分析师含 `name`、`displayName`、`temperature`、`topP`、`topK`、`instructions` 字段，运行时热加载。
- `config/models.yaml`：AI 模型与供应商配置。

---

## 构建与运行

```bash
# 构建
dotnet build src/MarketAssistant.App/MarketAssistant.App.csproj -c Debug

# 运行
dotnet run --project src/MarketAssistant.App/MarketAssistant.App.csproj -c Debug
```

可选：安装 Playwright CLI（用于首次拉起浏览器依赖，`Services/Browser/` 使用）：

```bash
dotnet tool update --global Microsoft.Playwright.CLI
playwright install
```
