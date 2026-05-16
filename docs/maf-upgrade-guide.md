# MAF 框架升级与架构审查指南

本文档记录了将 Microsoft Agent Framework（MAF）从 **1.0.0** 升级到 **1.4.0** 的完整过程与架构审查结论。文档同时保留可复用的升级操作步骤，供后续版本升级参考。

> 说明：本文以当前仓库代码为准（.NET 10 / Avalonia / MAF Workflows），不涉及"subagent/多智能体递归调用"模式；本项目的"多 Agent"指的是 **MAF Workflow 中的多个并发/串行节点**。

---

## 1. 项目结构核对

根目录 `AGENTS.md` 定义的分层与依赖方向整体合理：

```
Core（无依赖）
 ↑
 ├── Rag → Core
 ├── DataProviders → Core
 ├── Agents → Core
 ├── App.Services → Core, Agents, DataProviders, Rag
 └── App → Core, Agents, DataProviders, App.Services, Rag
```

各层职责落实情况：

| 项目 | 职责 |
|------|------|
| `MarketAssistant.App` | UI 宿主（Avalonia View / ViewModel / UI 服务） |
| `MarketAssistant.App.Services` | 运行时编排（Agent 实现、Workflow、Tools 实现、MCP、交易引擎） |
| `MarketAssistant.Agents` | Agent 契约层（基类、属性、工具接口、模型） |
| `MarketAssistant.Rag` | RAG 能力（解析/清洗/分块/嵌入/检索/重排） |
| `MarketAssistant.DataProviders` | 外部 API 封装（Binance、CoinGecko 等） |
| `MarketAssistant.Trading` | 交易域抽象层（`IExchangeClient` + trading models） |

**结论**：结构合理。Agent 编排与交易引擎收敛到 `App.Services`，避免 UI 层污染。`MarketAssistant.Trading` 作为交易域抽象层，能避免将 Binance 具体实现渗透到上层，当前定位可接受。

---

## 2. MAF 版本升级详情

### 2.1 依赖变更

核心 MAF 包升级：

| 包名 | 升级前 | 升级后 |
|------|--------|--------|
| `Microsoft.Agents.AI` | 1.0.0 | **1.4.0** |
| `Microsoft.Agents.AI.Workflows` | 1.0.0 | **1.4.0** |
| `Microsoft.Agents.AI.Workflows.Generators` | 1.0.0 | **1.4.0** |

同步升级（解决 1.4.0 传递依赖约束 / NU1605 包降级）：

| 包名 | 升级前 | 升级后 |
|------|--------|--------|
| `Microsoft.Extensions.AI` | 10.4.1 | **10.5.0** |
| `Microsoft.Extensions.AI.OpenAI` | 10.4.1 | **10.5.0** |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.5 | **10.0.6** |

### 2.2 1.4.0 重要变更

来自 [Microsoft.Agents.AI 1.4.0 Release Notes](https://github.com/microsoft/agent-framework/releases) 中与本仓库相关的重点：

- OpenTelemetry 依赖升级（对可观测性更友好）
- Durable workflow HTTP 触发结果返回（本项目当前未用 Hosting 场景）
- **[Breaking] file-based skill scripts 支持 string[] 参数**（需检查 skills 是否存在脚本——见第 4 节）
- Declarative Workflows 增加 `HttpRequestAction`（本项目主要使用 imperative `WorkflowBuilder`）
- 新增 Hyperlight CodeAct 包（本项目未用）

---

## 3. 升级操作步骤（可复用）

若需在其他分支或后续版本重复执行类似升级，按以下步骤操作。

### Step 1：修改包版本

涉及的项目文件：

- `src/MarketAssistant.Agents/MarketAssistant.Agents.csproj`
- `src/MarketAssistant.App.Services/MarketAssistant.App.Services.csproj`
- `src/MarketAssistant.App/MarketAssistant.App.csproj`
- `src/MarketAssistant.Rag/MarketAssistant.Rag.csproj`（OpenAI 客户端包版本需与 MAF 一致）
- `src/MarketAssistant.Core/`、`src/MarketAssistant.DataProviders/`、`src/MarketAssistant.Trading/` 中的 `Microsoft.Extensions.Logging.Abstractions` 引用

需遵循的原则：

1. `Microsoft.Agents.*` 统一到同一个版本
2. `Microsoft.Extensions.AI*` 与 MAF 版本要求对齐，避免 NU1605 downgrade 警告
3. `Microsoft.Extensions.Logging.Abstractions` 与 MAF 的最低要求对齐

### Step 2：还原与构建

```bash
dotnet restore MarketAssistant.slnx
dotnet build MarketAssistant.slnx -c Debug
```

如果公司 NuGet 源不可用（如 `http://devops.lonsid.cn:8080/nuget` 返回 502），可临时指定源：

```bash
dotnet build MarketAssistant.slnx -c Debug ^
  --source https://api.nuget.org/v3/index.json ^
  --source E:\masa.specdoc\local-packages
```

### Step 3：编译验证

```bash
dotnet build src/MarketAssistant.App/MarketAssistant.App.csproj -c Debug
```

> 备注：`tests/` 目录的单元测试可能存在独立的历史问题，不应阻塞"运行时可编译"的结论。本次升级后 `src/` 已通过编译。

---

## 4. Breaking Change 检查：Skills 脚本

MAF 1.4.0 的 breaking change 主要针对 **file-based skill scripts** 参数类型变化。

本仓库的 skills 输出目录位于 `src/MarketAssistant.App/skills/`，当前内容全部为 Markdown 引导/参考文件，无 `.csx`/`.ps1`/`.py` 等脚本资源：

- `skills/market-analysis/SKILL.md`
- `skills/crypto-trading/references/RISK_MANAGEMENT.md`

运行时通过 `AgentSkillsProvider` 从输出目录加载（注册于 `src/MarketAssistant.App.Services/Services/ServiceCollectionExtensions.cs`）：

```csharp
new AgentSkillsProvider(skillPath: Path.Combine(AppContext.BaseDirectory, "skills"))
```

**结论**：breaking change 不影响本仓库。

---

## 5. Agent 编排检查

### 5.1 市场分析：Fan-Out / Fan-In 并发工作流

实现文件：[MarketAnalysisWorkflow.cs](file:///c:/Users/mayue/Desktop/MarketAssistant/src/MarketAssistant.App.Services/Agents/MarketAnalysis/MarketAnalysisWorkflow.cs)

核心编排为标准的 Fan-Out / Fan-In 模式：

```
Dispatcher（入口 Executor）→ 多个 AIAgent（并发分析师）→ Aggregator → Coordinator → 输出
```

这是"多节点 Workflow"，不是"agent 调 agent 的递归链"，符合不使用 subagent 的约束。

额外特性：
- 动态按用户设置启用分析师
- `RequiredAnalystAttribute` 标记"必需分析师永远启用"
- `MarketSnapshotContextProvider` 在分析师间共享快照上下文

### 5.2 投资选股：串行工作流

实现文件：[InvestmentSelectionWorkflow.cs](file:///c:/Users/mayue/Desktop/MarketAssistant/src/MarketAssistant.App.Services/Agents/InvestmentSelection/InvestmentSelectionWorkflow.cs)

串行 executor 链路：`Generate criteria → Screen → Analyze`，通过 `MarketType` 分支选择 crypto vs A 股的 criteria 生成策略。

**结论**：编排清晰，职责分离合理。

### 5.3 对话：单 Agent + 中间件

实现文件：[MarketChatSession.cs](file:///c:/Users/mayue/Desktop/MarketAssistant/src/MarketAssistant.App.Services/Agents/MarketChatSession.cs)

单个 `ChatClientAgent`（或通过 `DelegatingAIAgent` 代理）+ middleware（压缩、token、MCP 等），无 workflow 节点编排，属于常规 chat session。

---

## 6. Crypto 市场支持检查

### 6.1 Keyed Services（主策略，正确）

Crypto 模块注册集中于 `src/MarketAssistant.App.Services/Services/Market/CryptoMarketModule.cs`：

- 以 `MarketType.Crypto` 为 key 注册工具实现与业务服务
- 上层工厂（如 `AnalystAgentFactory`）通过 `MarketContext.CurrentMarket` 获取 keyed service，避免业务层 `if/else`

**结论**：符合 `AGENTS.md` 的"多市场架构"约定。

### 6.2 交易所抽象与 Binance 实现（正确）

- 抽象：`MarketAssistant.Trading.Abstractions.IExchangeClient`
- 实现：`BinanceExchangeClient`（在 `App.Services` 内注入到 keyed 服务）
- 用途：Tools（如 `CryptoTradingExecutionTools`）→ `TradeExecutor` → `IExchangeClient` 下单

### 6.3 需持续关注：Keyed vs 非 Keyed 注入

`TradeExecutor` 显式使用 `[FromKeyedServices(MarketType.Crypto)] IExchangeClient exchangeClient`，保证了自主交易链路一定使用 Crypto 的交易所实现。

**建议**：对所有使用 `IExchangeClient` 的注入点做统一梳理，优先使用 keyed 注入，避免某些 ViewModel/Service 误注入非 keyed 实例导致运行时解析失败。

---

## 7. 自主交易模块审查

关键实现位于 `src/MarketAssistant.App.Services/Trading/` 与 `src/MarketAssistant.App.Services/Agents/Trading/`：

| 组件 | 职责 |
|------|------|
| `MarketMonitor` | 订阅 WebSocket，按 tick 触发策略 |
| `StrategyEngine` | 策略评估与触发条件判断 |
| `RiskManager` | 风控校验 |
| `TradeExecutor` | 统一下单入口（风控 → 确认 → 下单 → 持久化） |
| `TradingAgent` | 仅用于 `AISignal` 策略的 AI 决策与工具调用 |

### 7.1 运行机制

**MarketMonitor**：
- 使用 `Channel<(string Symbol, decimal Price)>` 缓冲 price update
- 单 reader 处理 tick（`SingleReader = true`），避免并发乱序
- 启动时从 `TradingDataService` 拉取 Active 策略并订阅对应 symbol

**TradeExecutor**：
- 统一入口 `ExecuteOrderAsync`，所有交易路径（策略触发、AI、手动）都应走这里
- 内置 Human-in-the-Loop：`ConfirmationCallback` 可由 UI 注入（未注入则默认拒绝需要确认的单）

**TradingAgent**：
- 继承 `DelegatingAIAgent`，工具集合来自 `ITradingExecutionTools` / `IStrategyTools` / `IBasicDataTools` / `ITechnicalDataTools`
- 低温度参数，强调"宁可不交易"

### 7.2 结论与改进建议

整体实现思路合理，核心优势：

- `TradeExecutor` 统一收敛"真实下单入口"
- `RiskManager` 做风控门禁
- `MarketMonitor` 用 channel 解耦实时行情与策略评估
- `AISignal` 策略通过工具调用实现"可审计的交易路径"，而非让模型直接拼 HTTP

改进建议（按优先级排列）：

1. **并发与幂等**：对同一 symbol 多策略同时触发的情况，在 `TradeExecutor` 引入 per-symbol 锁或幂等检查，避免重复下单。
2. **监控健壮性**：`MarketMonitor` 是单点后台循环，应增加健康检查与异常自恢复（或改为 `BackgroundService` + supervised restart）。
3. **AISignal 上下文记忆**：目前每次触发创建 agent 且无短期记忆，建议将最近 N 次决策/成交作为 context provider 注入（数据来源：`TradingDataService`）。
4. **策略覆盖面**：若存在 `GridTrading` / `DCA` 等未完成策略，在 `StrategyEngine` 中显式 `NotSupportedException` 或标记为 `Paused`，避免"看似支持但实际无效果"。

### 7.3 已落地的代码改动

以下改动随 MAF 升级同步实现：

| 改动 | 文件 / 位置 | 对应建议 |
|------|-------------|----------|
| 同标的下单串行化 | `TradeExecutor.ExecuteOrderAsync` | #1 并发与幂等 |
| AISignal 上下文增强 | `MarketMonitor.HandleAISignalAsync` | #3 上下文记忆 |
| 公开 `AddNamedMarketHttpClients` | `BusinessServiceCollectionExtensions` | 消除 `InternalsVisibleTo` 依赖 |
| 文档化 Crypto-only 工具解析 | `TradingAgentFactory` XML 注释 | 明确 Keyed 注入边界 |

---

## 8. 验证清单

### 编译与基础功能

- [ ] `dotnet build src/MarketAssistant.App/MarketAssistant.App.csproj -c Debug` 通过
- [ ] 启动 App，进入"市场分析"页面，执行一次分析，观察多分析师并发输出
- [ ] 进入"投资选股"功能，分别在 A 股与 Crypto 市场跑一次选股
- [ ] Crypto 市场打开交易监控，启停 `MarketMonitor`，确认 WebSocket 行情订阅正常

### 交易行为

- [ ] Risk 配置触发 `NeedsConfirmation` 时，UI 能正常处理 `ConfirmationCallback`（若 UI 未接入，预期为拒绝交易）
- [ ] `AISignal` 策略触发时，`TradingAgent` 能调用工具并通过 `TradeExecutor` 下单（建议在沙盒/测试网环境验证）

---

## 9. 未来可选优化

以下优化不阻塞当前升级，可在后续迭代中推进：

1. **Central Package Management**：引入 `Directory.Packages.props`，统一管理所有包版本，降低后续升级成本。
2. **App 项目依赖精简**：UI 项目尽量通过 `ProjectReference` 继承依赖，减少直接包引用，避免版本漂移。
3. **可观测性增强**：结合 MAF 1.4.0 的 OTel 升级，接入 OTLP/Jaeger exporter，统一 traceId 打通 Serilog，便于线上排查 agent/workflow 行为。
