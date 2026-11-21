# AGENTS.md

面向代码智能体（Agents）的专用说明文件。本项目为基于 Avalonia UI 的跨平台桌面应用，支持 A 股与虚拟币两个市场。各子项目拥有独立的 `AGENTS.md`，就近原则生效——请优先参阅离改动最近的 `AGENTS.md`。

---

## 项目结构

```
MarketAssistant.slnx
├── src/MarketAssistant.App/            ← UI 宿主（Avalonia）            → 有独立 AGENTS.md
├── src/MarketAssistant.App.Services/   ← 应用运行时/业务编排层         → 有独立 AGENTS.md
├── src/MarketAssistant.Core/           ← 核心抽象层（MarketType 等）    → 有独立 AGENTS.md
├── src/MarketAssistant.Agents/         ← Agent 契约层（MAF）            → 有独立 AGENTS.md
├── src/MarketAssistant.Rag/            ← RAG 基础能力层                → 有独立 AGENTS.md
├── src/MarketAssistant.DataProviders/  ← 数据提供者（Binance 等）      → 有独立 AGENTS.md
├── src/MarketAssistant.Trading/        ← 交易抽象与共享交易模型         → 有独立 AGENTS.md
├── src/MarketAssistant.Infrastructure/ ← 基础设施层（模型发现、Token 化）
├── tests/                              ← 单元测试工程
├── scripts/                            ← 构建脚本
└── docs/                               ← 设计文档
```

依赖方向：

```
Core（无依赖）
 ↑
 ├── Rag → Core, Infrastructure
 ├── DataProviders → Core
 ├── Agents → Core, Trading, Infrastructure
 ├── Infrastructure → Core
 ├── Trading → Core
 ├── App.Services → Core, Agents, Trading, DataProviders, Rag, Infrastructure
 └── App → Core, Agents, Trading, DataProviders, App.Services, Rag
```

核心技术栈：.NET 10 / C# 13 · Avalonia 12.x · Microsoft Agent Framework (MAF) · Semantic Kernel SQLiteVec · Serilog

---

## 开发环境

- .NET SDK 10.0+
- 无需额外工作负载，Avalonia 通过 NuGet 提供

```bash
dotnet restore MarketAssistant.slnx
dotnet build MarketAssistant.slnx -c Debug
dotnet run --project src/MarketAssistant.App/MarketAssistant.App.csproj -c Debug
```

---

## 智能体验证策略

完成代码编辑后，**必须**根据改动类型选择验证方式：

| 改动类型 | 验证方式 |
|---------|---------|
| `src/` 下任何 `.cs`、`.axaml`、`.csproj`、`config/` | `dotnet build MarketAssistant.slnx -c Debug` |
| 用户明确要求 或 重大架构重构 | `dotnet test tests/TestMarketAssistant.csproj -c Debug` |
| 文档、注释、资产文件 | 无需验证 |

---

## 通用代码风格（C# 13 / .NET 10）

- 所有子项目共享 `RootNamespace = MarketAssistant`，新增文件的命名空间按目录结构拼接（如 `MarketAssistant.Infrastructure.Core`），而非项目名。
- 仅在函数级添加文档注释；仅对晦涩逻辑添加必要行上方说明，不写赘余注释。
- 命名清晰、可读；优先完整词汇，避免缩写；异步方法以 `Async` 结尾。
- 控制流优先使用早返回与卫语句，避免深层嵌套；不要吞并异常。
- 避免 `TODO` 留存，能实现则实现；公共 API 明确类型标注。
- 变更应保持现有依赖注入与分层结构。
- 代码格式检查：`dotnet format --verify-no-changes`，自动修复：`dotnet format`。

---

## 简化优先的设计规范（强制）

目标：代码只做必要的事。新增与重构都按下列四条执行，评审时以此为准。

### 1. 防过度防御

- 默认**不为"理论上更稳"添加防御代码**（null/空值/边界检查、并发锁、重试、降级分支）。
- 几乎不会发生的边界或并发问题，先向用户说明**触发场景、发生概率、不加的后果**，经用户确认后再加；严禁私自膨胀代码。
- 以下属于架构上有意的防御，**不算过度防御，必须保留**：
  - 资金安全链、紧急停止（kill switch）、风控拒单（交易模块）。
  - 分析师失败隔离、`AnalysisAggregatorExecutor` 降级、`ResolveRuntimeForRole` 回退。
  - 告警通道、MCP 服务器、批量接口的**逐项隔离**；后台循环（轮询、WebSocket、新闻更新）的**单次失败隔离**。
  - RAG 向量与模型调用降级（见 `MarketAssistant.Rag/AGENTS.md`）。
  - 不可信文件解析（PDF/DOCX）、`Dispose` 清理、`GlobalExceptionHandler` 与应用启动/退出处理。

### 2. 入口单次校验

- 外部数据（用户输入、LLM Tool 参数、HTTP/WS 响应、文件、JSON）**只在边界校验一次**并转成强类型，内部靠类型系统与可空注解（`Nullable` 已启用，`TreatWarningsAsErrors=true`）流转。
- 禁止：DI 构造函数 `?? throw new ArgumentNullException`；内部方法对非可空参数 `ThrowIfNull` / `IsNullOrWhiteSpace`；多层重复 `Normalize` / `Trim` / 同一标的重复校验。

### 3. 拒绝提前抽象

- 仅使用一次的逻辑**就近内联**；只转发调用的 helper、wrapper、Provider、Factory 不要建。
- 同一模式出现 **3 次及以上**且语义一致，才提取为共享组件；不为单一实现新增接口，除非需要 Keyed 市场变体或测试替身。

### 4. 控制复杂度

- 非必要不用 `try/catch`，让异常自然向上传播，只在统一落点处理：
  - Tool 方法 → `ToolExecutor.ExecuteAsync`
  - ViewModel 加载 → `ViewModelBase.LoadAsync`；ViewModel 命令 → `SafeExecuteAsync`
  - 后台循环 → 循环体一处 `catch`
  - 业务服务 → 不捕获，直接向上传播
- 禁止"只记日志后原样 `throw`"，禁止"吞异常后返回空集合 / `false` / `null`"（与 App.Services 的「空」与「失败」必须可区分一致）。
- 能在单个函数内清晰完成的逻辑不要硬拆。

---

## 避免重复造轮子

- 新增基础能力前，先检索项目内现有抽象、服务和已引入 NuGet 依赖，优先复用，不要在业务层重新实现一套。
- 对 HTTP、缓存、配置、消息分发、技术指标、文档解析等通用能力，优先使用成熟开源库或项目内统一组件。
- 禁止在 ViewModel 或业务服务中直接手写新的 retry、timeout、限流、缓存、配置加载框架；优先复用 `HttpClient`、resilience pipeline、`IMemoryCache`、统一设置服务。
- 技术指标优先复用统一指标组件或成熟库，不要在多个服务重复实现 `MA`、`EMA`、`MACD`、`BOLL`、`KDJ` 等公式。
- 引入新三方库时，优先选择社区成熟、维护活跃、与当前技术栈兼容的方案，并在代码或文档中说明替换理由与迁移风险。

---

## 安全与配置

- API 密钥（Binance、AI 模型等）通过应用内设置界面输入，持久化在用户本地目录，**禁止提交到仓库**。
- 不要提交 `.env`、`appsettings.*.json`、用户偏好文件等包含密钥的文件。
- DI 注册入口：`Program.ConfigureServices()` → `src/MarketAssistant.App/Services/ServiceCollectionExtensions.cs`。
- 业务服务注册入口：`AddApplicationServices()` 内部调用 `AddBusinessServices()`，后者定义在 `src/MarketAssistant.App.Services/Services/ServiceCollectionExtensions.cs`。
- RAG 注册入口：`src/MarketAssistant.Rag/Extensions/ServiceCollectionExtensions.cs` 中的 `AddRagServices()`。

---

## 多市场架构

- 支持 **A 股**（`MarketType.AShare`）和 **虚拟币**（`MarketType.Crypto`）。
- 使用 .NET **Keyed Services**（`IServiceProvider.GetRequiredKeyedService<T>(MarketType)`）实现同一接口的市场特定实现。
- `MarketContext` 单例管理当前活跃市场。
- 新增市场需在 `MarketType` 枚举添加值，并为所有 Keyed Service 接口注册新实现。

---

## PR 与提交规范

- 提交信息格式：`[模块] 变更概要`，例如：`[Agents] 新增情绪分析工具接口`。
- 所有代码改动需确保构建通过。
- 不要在仓库中提交密钥或令牌。
- 对基础设施或脚手架的新增，请在相应 `AGENTS.md` 或 `README.md` 补充说明。

---

## 发布

详见 [`scripts/BUILD.md`](scripts/BUILD.md)。快速命令：

```powershell
# Windows 一键发布
powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1

# 手动跨平台发布
dotnet publish src/MarketAssistant.App/MarketAssistant.App.csproj -c Release -r win-x64 --self-contained
dotnet publish src/MarketAssistant.App/MarketAssistant.App.csproj -c Release -r osx-x64 --self-contained
dotnet publish src/MarketAssistant.App/MarketAssistant.App.csproj -c Release -r linux-x64 --self-contained
```

---

## 测试

```bash
# 全量测试
dotnet test tests/TestMarketAssistant.csproj -c Debug --logger "trx;LogFileName=TestResults.trx"

# 过滤运行
dotnet test tests/TestMarketAssistant.csproj --filter FullyQualifiedName~StockServiceTest
```

---

## FAQ

- **测试是否为必需？** 不是。智能体默认只确保编译通过，测试为可选项。
- **子目录的 AGENTS.md 如何生效？** 就近原则——离改动文件最近的 `AGENTS.md` 优先。
- **设计文档放哪？** `docs/` 目录，不放入 AGENTS.md。
