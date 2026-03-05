# AGENTS.md

面向代码智能体（Agents）的专用说明文件。本项目为基于 Avalonia UI 的跨平台桌面应用，支持 Windows、macOS 和 Linux 平台，以及独立的测试工程。智能体应根据改动类型选择合适的验证方式，确保提交前代码可正常构建。

---

## 一、项目概览

- 解决方案：`MarketAssistant.slnx`
- 主项目（Avalonia 应用）：`src/`
- 单元测试工程：`tests/`
- 构建脚本：根目录 `build-release.ps1`，构建说明见根目录 `BUILD.md`

核心技术栈：

- **UI 框架**：Avalonia 11.x
- **AI 框架**：Microsoft Agent Framework (MAF)（`Microsoft.Agents.AI` + `Microsoft.Agents.AI.Workflows`）
- **向量存储**：Semantic Kernel SQLiteVec
- **日志**：Serilog
- **多市场架构**：通过 `MarketType` 枚举 + .NET Keyed Services 实现 A 股/虚拟币市场的统一抽象与动态切换

主要功能模块：

- 业务与设置：`src/Applications/`
- 视图与视图模型：`src/Views/`, `src/ViewModels/`
- 智能体分析师角色：`src/Agents/Analysts/`（含多种分析师实现）
- 市场分析工作流：`src/Agents/MarketAnalysis/`（Fan-Out/Fan-In 并发分析）
- 投资选择工作流：`src/Agents/InvestmentSelection/`（三步骤确定性工作流）
- 代理与会话：`src/Agents/`
- 资源与样式：`src/Resources/Styles/`
- 资产文件：`src/Assets/`
- 模型配置：`src/config/models.yaml`

---

## 二、开发环境与准备

### 1. 必备工具

- .NET SDK 10.0（或以上）
- 无需额外工作负载，Avalonia 通过 NuGet 包提供

```bash
dotnet --info
```

可选：安装 Playwright CLI（用于首次拉起浏览器依赖）

```bash
dotnet tool update --global Microsoft.Playwright.CLI
playwright install
```

### 2. 还原与编译

```bash
dotnet restore MarketAssistant.slnx
dotnet build MarketAssistant.slnx -c Debug
```

或针对主项目：

```bash
dotnet restore src/MarketAssistant.csproj
dotnet build src/MarketAssistant.csproj -c Debug
```

### 3. 运行（开发）

跨平台运行（Windows/macOS/Linux）：

```bash
dotnet run --project src/MarketAssistant.csproj -c Debug
```

---

## 三、测试与质量检查

### 1. 智能体验证策略（必需）

智能体在完成代码编辑后，**必须**根据改动类型执行验证：

#### A. 必须执行构建验证 (`dotnet build`)
涉及以下目录或文件的修改，必须确保编译通过：
- **业务逻辑**：`src/Applications/`, `src/Services/`, `src/Agents/` (含 Plugins)
- **基础设施**：`src/Infrastructure/`, `src/Rag/`, `src/Models/`, `src/Parsers/`
- **UI 层**：`src/Views/`, `src/ViewModels/`, `src/Resources/`, `src/Converts/`
- **配置**：`src/config/`

#### B. 可选执行单元测试 (`dotnet test`)
仅在以下情况执行：
- 用户明确要求执行测试
- 进行重大架构重构
- 修复已知的测试失败问题

#### C. 无需验证
- 文档（README.md, AGENTS.md 等）
- 纯注释修改
- 资产文件（图片等）

### 2. 运行全部测试

```bash
dotnet test tests/TestMarketAssistant.csproj -c Debug --logger "trx;LogFileName=TestResults.trx"
```

### 3. 按名称过滤运行

```bash
dotnet test tests/TestMarketAssistant.csproj --filter FullyQualifiedName~StockServiceTest
```

### 4. 代码格式（若需）

```bash
dotnet format --verify-no-changes
# 如需自动修复：
dotnet format
```

---

## 四、配置与运行时约定

- 模型与供应商配置：`src/config/models.yaml`

多市场架构：

- 项目支持 **A 股**（`MarketType.AShare`）和 **虚拟币**（`MarketType.Crypto`）两个市场。
- 使用 .NET **Keyed Services** 模式（`IServiceProvider.GetRequiredKeyedService<T>(MarketType)`）实现同一接口的市场特定实现。
- `MarketContext` 单例管理当前活跃市场，UI 和业务层通过它获取当前市场类型。
- 新增市场支持时，需在 `MarketType` 枚举添加值，并为所有 Keyed Service 接口注册新实现。

外部 API 依赖（虚拟币市场）：

- **Binance API**（`api.binance.com` / `fapi.binance.com`）：实时行情、K 线、订单簿、资金费率、多空比等。某些地区受限，需通过 VPN 或代理访问。
- **CoinGecko API**（`api.coingecko.com`）：市值、排名、多时间段涨跌幅等。免费版有频率限制。
- **CoinDesk API**（`data-api.coindesk.com`）：项目基本面、新闻数据。

建议：

- 不要在仓库中提交任何密钥或令牌。密钥应通过应用内设置页或安全存储注入。
- 如引入新外部依赖，需在 README 或本文件中注明安装步骤与运行前置条件。
- 虚拟币相关 API 可能需要网络代理，请确保开发环境能访问上述域名。

---

## 五、代码风格与工程约束

通用（C# 13 / .NET 10）：

- 仅在函数级添加文档注释；仅对晦涩逻辑添加必要行上方说明，不写赘余注释。
- 命名清晰、可读；优先完整词汇，避免缩写；异步方法以 `Async` 结尾。
- 控制流优先使用早返回与卫语句，避免深层嵌套；不要吞并异常。
- 避免 `TODO` 留存，能实现则实现；公共 API 明确类型标注。
- 变更应保持现有依赖注入与分层结构（`ViewModels`、`Applications`、`Services`、`Agents` 等）。

UI 与样式（Avalonia AXAML）：

- **文件格式**：视图文件必须使用 `.axaml` 扩展名。
- **布局约束**：
  - `Padding`/`Margin`/间距必须使用 **4 的倍数**（4/8/12/16），且原则上不超过 16。
  - **禁止硬编码数值**：布局数值应使用 `src/Resources/Styles/Spacing.axaml` 中的资源（如 `{StaticResource SmallMargin}`），或在 `UserControl.Resources` 中定义局部资源。
- **样式管理**：
  - 统一遵循 `src/Resources/Styles/` 中的集中式样式资源。
  - 避免在视图中硬编码颜色与字体。
- **资源引用规则**：
  - `Colors.axaml` 中 `Primary`、`PrimaryLight` 等是 **Color** 类型，用于 `Background`/`Foreground` 时必须引用对应的 **Brush**（如 `PrimaryBrush`、`PrimaryLightBrush`）。
  - 主题感知资源（`PageBackgroundBrush`、`TextPrimaryBrush` 等在 `ThemeDictionaries` 中定义）使用 `{DynamicResource}`。
  - 固定资源（间距、尺寸、圆角等）使用 `{StaticResource}`。
- **控件使用**：优先使用 Avalonia 内置控件，必要时参考现有自定义控件。
- **资产管理**：非必要不改动图片与资产文件；若必须更改，需监控构建体积。

ViewModel 编码规范：

- 使用 `CommunityToolkit.Mvvm` 的 `[ObservableProperty]`、`[RelayCommand]` 等源生成器特性。
- `ICommand` 属性禁止在 getter 中每次创建新实例（如 `=> new RelayCommand(...)`），应在构造函数中初始化或使用静态实例。
- 订阅外部事件（如 `PropertyChanged`、`CollectionChanged`）的 ViewModel 应实现 `IDisposable`，在 `Dispose` 中取消订阅。
- `ObservableCollection` 属性应在构造函数中初始化一次，不应在 getter 中每次创建新实例。
- 异常处理：使用 `SafeExecuteAsync` 或 `ErrorMessageMapper`，**不要吞并异常**（空 `catch` 块）。
- ViewModel 通过 DI 容器获取依赖，不要使用 `ServiceLocator` 或直接 `new` 服务实例。

---

## 六、目录导航（常用）

- 代理与分析角色：`src/Agents/Analysts/`（多种分析师实现）
- 市场分析工作流：`src/Agents/MarketAnalysis/`（Fan-Out/Fan-In 并发分析 Workflow）
- 投资选择工作流：`src/Agents/InvestmentSelection/`（三步骤确定性 Workflow + 市场特定 Strategy）
- Agent 工具：`src/Agents/Tools/`（按市场分组：`AShare/`、`Crypto/`，接口在 `Abstractions/`）
- 业务能力：`src/Applications/`（资产信息、筛选、K 线、收藏、历史、快讯、投资选择等）
- 外部 API 服务：`src/Services/Data/`（Binance、CoinGecko、CoinDesk API 封装）
- 视图与 VM：`src/Views/`, `src/ViewModels/`
- 资源与样式：`src/Resources/Styles/`（Avalonia 样式资源字典）
- 资产文件：`src/Assets/`（图片、图标、HTML 等）
- 类型转换器：`src/Converts/`
- 基础设施：`src/Infrastructure/`（配置、核心工具类、工厂、符号转换器等）
- 服务层：`src/Services/`（浏览器、缓存、市场上下文、对话框、导航、MCP 等）
- RAG 相关：`src/Rag/`（向量化与检索增强生成）
- 测试：`tests/`

---

## 七、Agent 与工具编码约定

新增或修改 Agent、工具时遵循以下约定：

- **工具注册**：工具接口定义在 `src/Agents/Tools/Abstractions/`，市场特定实现注册为 Keyed Service（key = `MarketType`），通用工具直接注册为 Singleton。
- **分析师工具声明**：分析师通过 `[RequiresTools(typeof(IXxxTools))]` 属性声明所需工具，`AnalystAgentFactory` 自动按当前市场类型从 DI 容器解析。新增分析师必须遵循此模式。
- **ChatSession 上下文注入**：分析结果通过 `MarketChatSession.InjectAnalysisContext()` 注入系统指令，不要手动操作 MAF 的 `AgentSession` 内部历史。
- **ChatSession 工具范围**：`MarketChatSession` 仅持有 `GroundingSearchTools` + MCP 工具，不持有市场数据工具（市场工具在分析 Workflow 阶段由各分析师使用）。
- **版本号**：定义在 `src/MarketAssistant.csproj` 的 `<Version>` 属性中，运行时通过 `AppInfo.Version` 获取。
- **设计文档**：功能设计、架构规划等文档放在 `docs/` 目录下，不放入 AGENTS.md。

---

## 八、发布与打包

### Windows 发布

使用一键发布脚本（PowerShell）：

```powershell
powershell -ExecutionPolicy Bypass -File .\build-release.ps1
```

### 跨平台发布

手动发布到特定平台：

```bash
# Windows
dotnet publish src/MarketAssistant.csproj -c Release -r win-x64 --self-contained

# macOS
dotnet publish src/MarketAssistant.csproj -c Release -r osx-x64 --self-contained

# Linux
dotnet publish src/MarketAssistant.csproj -c Release -r linux-x64 --self-contained
```

更详细的发布与生成说明请参阅根目录 `BUILD.md`。

---

## 九、PR 与提交规范

- 提交信息建议格式：`[模块] 变更概要`，例如：`[Plugins] 新增资金流插件与测试`。
- 所有代码改动需确保构建通过；单元测试为可选，由开发者根据实际情况决定是否执行。
- 如涉及平台相关改动，至少在一个目标平台（Windows 或 macOS）完成启动验证。
- 对基础设施或脚手架的新增，请在 `README.md` 或本文件补充对应说明与命令。

---

## 十、常见问题（FAQ）

- Q：测试是否为必需？
  - A：不是必需的。智能体默认只确保代码编译通过，测试为可选项，仅在用户明确要求或重大重构时执行。
- Q：智能体会自动执行测试吗？
  - A：不会。智能体只会执行 `dotnet build` 确保编译通过，不会自动执行单元测试，除非用户明确要求。
- Q：如何运行特定模块的测试？
  - A：使用过滤器，例如：`dotnet test --filter FullyQualifiedName~StockServiceTest`
- Q：是否可为子目录添加更细化的 AGENTS.md？
  - A：可以。若在子项目放置更近的 `AGENTS.md`，就近原则生效。
