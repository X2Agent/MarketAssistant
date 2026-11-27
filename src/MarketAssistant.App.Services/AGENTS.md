# MarketAssistant.App.Services — AGENTS.md

桌面应用的运行时与业务编排层。负责 Agent 实现、Workflow、Tool 实现、MCP、交易编排、业务服务与运行时基础设施；**不承载 Avalonia 视图、导航控件或页面样式**。

---

## 目录结构

```
MarketAssistant.App.Services/
├── Agents/                        ← Agent 实现、工作流、执行器、工具实现
├── Applications/                  ← 面向 UI 的业务服务（Assets/Home/Favorites/Analysis/AlertCenter 等）
├── Infrastructure/                ← 工厂、配置、通用运行时适配
├── Services/                      ← 横切服务（Archive/Browser/Cache/Market/Mcp/Settings 等）
└── Trading/                       ← 交易引擎、风控、持久化与监控
```

---

## 边界约定

- 本项目承载除 UI 和独立基础模块外的大部分运行时代码；新增 Agent、Tool、Workflow、MCP、交易服务时，优先放在这里。
- Avalonia 视图、窗口、导航、通知、对话框实现属于 `MarketAssistant.App`，不要回流到本项目。
- 文档解析、清洗、分块、嵌入、检索、重排等 RAG 基础能力属于 `MarketAssistant.Rag`，不要在本项目再复制一套实现。
- 市场特定能力通过 `MarketType` Keyed Services 注册，不在业务逻辑中堆叠 `if/else` 区分市场。
- Tool 接口定义保留在 `MarketAssistant.Agents/Tools/Abstractions/`，本项目只放实现。
- Tool 方法统一通过 `ToolExecutor.ExecuteAsync` 执行（日志、计时、`FriendlyException` 转换集中处理），不要在每个 Tool 方法里手写 `try/catch`。
- 对外部 HTTP/API 的直接调用优先收敛到 `MarketAssistant.DataProviders` 或已存在的统一服务中，不在多个 Tool/Service 内重复实现。

### 「空」与「失败」必须可区分（强制约定）
数据服务禁止把异常吞成空集合或占位对象——那会让"数据源不可用"在 UI 上伪装成"没有数据"。

- 返回空集合 / `null` / 默认占位对象，语义限定为**数据源正常但查无数据**。
- 数据源故障、解析失败、超时等一律向外抛出。需要保留取消语义时先 `catch (OperationCanceledException) { throw; }` 再包装其余异常为 `FriendlyException`（携带原始异常）。
- 批量接口（如 `IAssetInfoService.GetAssetInfosAsync`）按项隔离：单项失败占位为 `null`，不得让整批失败。
- **多源降级例外**：`AShareMarketSnapshotService`（财联社失败回退新浪）与 `CryptoScreenerService`（CoinGecko 不可用降级 Binance 兜底）属产品级多源回退——主源失败按源隔离后走备源、备源仍失败才向外抛出，不算吞异常。
- 编排层（`Applications/`）负责把异常翻译成 UI 状态，UI 层不再二次判断。

---

### 职责不重叠：同一分析维度只由一个分析师持有工具（强制约定）

同一个分析维度只能由一个分析师持有对应工具，否则会重复取数并把输入体积成倍放大。

- 反例（已修复）：`MarketSentimentAnalystAgent` 曾同时挂 `[RequiresTools(typeof(IFinancialTools))]` 与 `[RequiresTools(typeof(ISentimentTools))]`。A 股下它的 6 个工具里 5 个是与 `FinancialAnalystAgent` 重复的财务宽表工具，叠加四个并列子对象的输出契约，一次运行输入达 **26.6 万 Token**，运行约 53 秒即触发 `TokenTrackingMiddleware.MaxTokensPerRun`（200K）熔断，该维度降级。
- 重复挂载是放大器，而非唯一变量：同配置在更早一次运行里只用了约 3 万 Token，是否熔断还取决于模型这次跑了几轮工具循环（每一轮都会把已取回的宽表重新带入上下文）。同一次运行里财务分析师挂同样 5 个宽表工具，32 秒完成、未熔断，说明问题在"重复"，不在"宽表"本身。
- 结论：修复方式是让情绪分析师只挂 `ISentimentTools`（财务维度归 `FinancialAnalystAgent`），**不是**给工具加投影层。曾尝试把五张宽表投影成单一「财务概览」工具替代原 5 个工具，但日志证明财务分析师本就没有超限，该改动解决的是假设出来的问题，且会额外引入「一次拉 5 张表」的开销，已回退。
- 新增分析师时检查：`[RequiresTools]` 声明的接口是否已被其他分析师持有；同一维度重复挂载是熔断的主要来源。
- 工具输出的体积治理应放在通用层（如 `ToolExecutor` 统一截断），而不是逐个工具重写数据契约——后者会把「按需深取明细」的能力一并削掉。

---

## 市场行情区块服务（Applications/Assets）

详情页信息密度的数据来源，均为 **Keyed Services**（按 `MarketType` 注册，A 股与 Crypto 各一套实现）。

| 接口 | 职责 | 注册 |
|------|------|------|
| `IMarketSnapshotService` | 行情速览（今开/最高/最低/昨收/量额/换手/振幅/量比/PE/PB） | `AShareMarketSnapshotService` / `CryptoMarketSnapshotService` |
| `ITechnicalIndicatorService` | 技术指标快照（MA/MACD/BOLL/KDJ） | 市场无关实现（`TechnicalIndicatorService`，复用 `Skender.Stock.Indicators`），数据不足时返回 `null` 而非抛异常 |
| `IOrderBookService` | 盘口深度 | Crypto：`CryptoOrderBookService`（币安深度）；A 股：`AShareOrderBookService`（新浪个股行情五档）。A 股解析器带交叉校验，任一条不满足即返回 `null` |
| `ITickTradeService` | 逐笔成交 | Crypto：`CryptoTickTradeService`；A 股：`NoopTickTradeService`（新浪接口不提供逐笔明细，`SupportsTickTrades => false`，页面隐藏该区块）。**逐笔不含主动买卖方向**，UI 不得据 `IsBuyerMaker` 推断内外盘 |

> A 股 `SupportsOrderBook` 为 `true`（新浪五档）、`SupportsTickTrades` 为 `false`（字段 26..29 实为卖四/卖五档位，早期据错误假设解析出的"逐笔"只是卖盘档位，已移除）；
> Crypto 两项均为 `true`。`NoopTickTradeService`（A 股使用）是目前唯一的空实现；曾有一个无人注册、无人引用的 `NoopOrderBookService`，已删除——新增市场缺盘口数据源时，再按需添加返回 null 的实现。

- 新增市场区块服务时，必须在 `MarketContext` 对应的 MarketModule 中注册，并同步更新 `IMarketCapability` 的能力位。每个市场都必须注册全部 keyed 接口——不支持的市场注册 `Noop*` 实现（返回空集合），**不要**抛异常伪装成加载失败，也不要在业务层用 `if/else` 绕开解析。
- 不支持某能力的市场由页面显式说明数据源限制；区块之间必须可独立加载与独立失败，禁止把多个区块串成一个必须全部成功的事务。

---

## 每日 AI 简报（Applications/Briefing）

自选股的"要闻速览 + 异动归因"摘要，按市场 Keyed 注册（`IDailyBriefingService`），SQLite 表 `briefings` 按 `(market_type, date_key)` 唯一，当天生成后各端复用。**纯手动生成**：构造函数只读当天缓存，绝不自动触发模型调用。

- **重要性与相关性判断交给模型**：代码只做条数截断，**不做本地重要性/相关性过滤**。实测 A 股同花顺 `import` 字段 97% 为 0（无区分度）、88% 的快讯无关联股票（`stock` 为空或外盘），基于这些信号硬过滤等于用噪声决策。`Telegram.Symbols` 在 A 股因字段大小写不匹配恒为空，不可用。
- **要闻覆盖约 28~29 小时**：数据源 `pagesize=100`（A股）/`take=100`（币圈）实测跨度 28.9h / 28.3h；原先 `20` 条仅覆盖 2.1h / 9.2h，缺失隔夜与开盘前消息。
- **Token 预算硬边界（仅限输入侧）**：`DailyBriefingService` 的 `MaxFavorites/MaxNewsItems/MaxNewsTitleLength/MaxNewsContentLength` 常量钳制输入；自选按异动幅度优先纳入，要闻按数据源时间倒序取前 N 条。调大任何边界前先评估成本（要闻部分当前约 2100 token/次，为早期 8 条方案的约 5.5 倍）。
- **不设输出上限，也不截断正文**：本层所有 LLM 调用（简报、选股的筛选条件生成与候选资产分析、记忆抽取、市场分析）**不得**设置 `ChatOptions.MaxOutputTokens`。推理模型的思考 Token 与正文共用输出额度，任何固定值都无法保证够用，用尽即得到"已计费却无正文"的空响应（曾导致 OpenRouter 推理模型下简报持续失败；选股与记忆抽取里同类的固定上限已一并删除）。成本风险靠输入侧预算与模型选择控制，输出长度由服务商默认上限与客户端超时兜底（简报是单次、无工具调用，不会出现循环失控）。返回正文同样**不做字数硬截断**（曾有的 `MaxBriefingLength` 已删）：截断发生在生成之后并不省 Token，还会切断句子与 Markdown 并把残缺文本落库；字数由提示词软约束，超长由简报窗口的 `ScrollViewer` 兜底。
- **单次调用、无重试链**：经 `IChatClientFactory.CreateRuntime()` 默认模型一次 `GetResponseAsync` 完成；失败直接抛给调用方（UI 展示重试），不自动重试烧 Token，失败结果不落盘。
- **空正文必须带诊断抛出**：模型返回空正文时抛 `FriendlyException`（界面原样展示），消息含模型 ID、`FinishReason` 与输出/推理 Token 用量，用以区分"思考耗尽额度 / 上游错误 / 内容被拦截"；不得改回 `InvalidOperationException`——`ErrorMessageMapper` 会把它映射成与真因无关的"操作无效，请刷新后重试"。
- **空输入不是错误**：自选与要闻均空时返回 `null`，页面展示引导空态（"添加自选后……"），不要生成无意义内容。
- **缓存语义**：`GenerateAsync(force: false)` 命中当天缓存直接返回；日期键用**本地时间** `yyyy-MM-dd`（跨时区/零点行为与用户感知一致）。
- 提示词只允许给定事实，明确禁止编造与买卖建议——简报定位是事实归因，不是投资顾问。措辞上不使用"隔夜"：数据源虽覆盖跨日，但并不保证每条都落在隔夜时段。

---

## 成本估算（已移除）

2026-10 决策：**移除货币成本估算，只保留 Token 用量展示**。原 `Services/Cost`（`ModelCostCalculator` / `AnalysisCostService`）、`UserSetting.ModelPrices` 与 `DefaultModelPriceCatalog` 内置参考价均已删除，设置页「模型价格表」分区一并移除。

- 原因：价格表维护负担重、内置参考价快照会过期、未覆盖模型只能显示"部分未定价"，用户感知价值低于维护成本；Token 用量本身已足够支撑"这次分析花了多少算力"的判断。
- 旧设置文件 JSON 中的 `ModelPrices` 字段由反序列化默认忽略，下次保存自然消失，无需迁移。
- Token 用量的采集与展示（`ITokenUsageRecorder` / `TokenUsageFormatter` / 报告页与简报卡片）不受影响，仍在服役。
- **不要重新引入"按默认模型猜价格"的逻辑**：宁可只展示 Token 数，也不给错误的金额预期。

---

## 模型分级调度（Infrastructure/Factories/ChatClientFactory）

一次深度研判会并行启动多个分析师，全部使用同一个强模型既慢又贵。因此支持**按分析师角色**指定模型。

- 角色 key 约定为**分析师类型类名**（如 `FundamentalAnalystAgent`、`CoordinatorAnalystAgent`），与 `UserSetting.EnabledAnalystRoles`、设置页 `AnalystRoleViewModel.Id` 保持一致。
- 映射持久化在 `UserSetting.ProviderAnalystModelIds`（`AnalystModelAssignment`：角色 → 模型 ID），按 provider 分组。
- 解析入口 `ChatClientFactory.CreateRuntime(string? modelIdOverride = null)`：传 `null` 走 provider 默认模型。`MarketAnalysisWorkflow.ResolveRuntimeForRole` 负责查表并**吞掉解析失败降级为默认 runtime**——角色配置错误绝不能导致整次分析失败。
- 新增角色映射时必须同步更新 `AnalysisCacheService.GetConfigFingerprint()`：指纹由 `activeModelId | roleAssignments | enabledRoles` 组成。漏改会导致"换了模型却命中旧模型缓存"。
- runtime 缓存按指纹区分，**不设数量上限**：缓存项只是 `IChatClient`，几乎没有成本。曾设上限 32（此前 16 已不够用，被迫上调），超出即抛错让全部 AI 功能不可用直到重启，已删。不要在这里加淘汰——会释放仍被会话引用的客户端。

---

## LLM 限流与重试（Infrastructure/Factories/RateLimitAwareRetryPolicy）

OpenAI 协议客户端（`ChatClientFactory.CreateOpenAIClient`）的 SDK 级重试由 `RateLimitAwareRetryPolicy(3)` 接管；Ollama 走 Http.Resilience，不受影响。它只重写 SDK 官方扩展点（`ShouldRetryAsync` / `GetNextDelay`），**不要**在业务层另写 retry。

- **日配额不重试**：429 正文含 `free-models-per-day`（OpenRouter 免费模型日配额，UTC 零点即北京时间 08:00 重置）时立即放行给调用方——等多久都不会恢复，SDK 默认的 0.8/1.6/3.2 秒退避只会白白拖慢失败。错误文案由 `ErrorMessageMapper.TryGetProviderErrorMessage` 转成含重置时间与处理建议的中文，`MarketAnalysisWorkflow` 在协调者失败与分析师降级两处使用；其他异常保留原始 `Message`。
- **分钟级限流等到重置**：响应带 `X-RateLimit-Reset`（毫秒时间戳），且距现在不超过 90 秒、又比默认退避更久时，等到「重置时间 + 1 秒」再重试，并写一行 Warning（SDK 自身的重试事件不进 Serilog，有意的长等待必须可见）。头缺失、不可解析、已过期或超过 90 秒都沿用 SDK 默认（含 `Retry-After`）。该头格式取自 OpenRouter 文档，暂无本项目实测样本，遇到分钟级 429 时留意日志里的等待秒数是否合理。
- 与简报「单次调用、无重试链」不冲突：那条约束的是应用层不叠加重试链，传输层的 SDK 重试一直存在。
- `EmbeddingFactory` 仍用 SDK 默认 `ClientRetryPolicy(3)`，尚未接入；嵌入端点出现同类 429 时再复用该策略。

---

## Token 消耗透出

- 分析链路在 `MarketAnalysisWorkflow` 中通过 `ITokenUsageRecorder.BeginScope(runId)` 打开作用域，把各分析师的 Token 消耗归集到 `MarketAnalysisReport.Usage`。
- 作用域与归属约定见 `src/MarketAssistant.Agents/AGENTS.md`；本项目只负责开启作用域、传递与写入报告，不重复实现计量逻辑。

---

## DI 注册

- 业务注册根：`Services/ServiceCollectionExtensions.cs` 中的 `AddBusinessServices()`。
- UI 宿主通过 `MarketAssistant.App/Services/ServiceCollectionExtensions.cs` 调用本项目注册入口。
- 新增服务时，按职责放到对应扩展方法附近，避免把所有注册逻辑散落到 ViewModel 或窗口构造中。

---

## MAF 约定

- 分析师实现放在 `Agents/Analysts/`，统一通过 `IAnalystAgentFactory` 创建。
- 工作流实现放在 `Agents/*Workflow*/`，执行器放在 `Executors/`。
- 通用知识型 Skill 资源保留在 `MarketAssistant.App/skills/` 作为内容文件输出，本项目通过 MAF 内置 `AgentSkillsProvider` 加载，不在这里重复存放一份。
- MCP 工具由 `McpToolContextProvider` 注入聊天会话。`McpService.GetAIToolsAsync` 合并多个服务器的工具时，**跨服务器重名只保留先出现的**，后到的跳过并写审计日志：模型与 `FunctionInvokingChatClient` 都按名字解析工具，同名会把调用路由到错误的服务器，部分提供商还会因重复函数名拒绝整个请求。回归测试：`McpServiceRuntimeTest.GetAITools_SameToolNameAcrossServers_ShouldKeepFirstServerAndSkipDuplicate`。

### 分析师中间件顺序契约

`AnalystAgentFactory.CreateAnalyst` 是分析师中间件组合的**唯一入口**，顺序固定为：
`ChatClientAgent → ToolDrivenOutput（`AnalystAgentBase` 内部装配，仅挂工具时）→ TokenTracking（`WrapWithTokenTracking`）→ FailureIsolation（最外层，传入 onAnalysisFailed 回调时附加）`。
失败隔离必须在最外层：Token 熔断（单次运行 200K 的事后检查，见 `src/MarketAssistant.Agents/AGENTS.md`）等内层异常要被它转换为失败标记消息（Fan-In 聚合器仍能收齐消息），而非中断整次分析。**不要在工厂之外对分析师再单独包装中间件**。ToolDrivenOutput 是工具闸门 + 收尾结构化装饰器，位于 `ChatClientAgent` 之上、TokenTracking 之下，不改变上述顺序契约。回归测试：`tests/AnalystAgentFactoryMiddlewareOrderTest.cs`。

**调试提示**：一次模型失败（如 OpenRouter 404 模型下线）会沿 ToolDrivenOutput → TokenTracking → FailureIsolation 逐层 `await` 重抛，每位分析师仍只有 1 次模型调用、无重试（`tests/Infrastructure/RateLimitAwareRetryPolicyTest.cs` 的 `ModelNotFound404_ShouldNotRetry` 守护）。VS 开启「引发时中断」时，同一次失败每经过一层就停一次（再乘以并行的分析师数），这是 .NET 异步重抛在调试器里的正常表现，不是重复处理；在「异常设置」里取消 `System.ClientModel.ClientResultException` 的「引发时中断」即可。不要为此把 catch 下沉到内层，那会破坏上述顺序契约。

### MAF 升级复核清单

以下 8 处代码依赖 MAF 框架内部行为（非稳定契约），升级 `Microsoft.Agents.AI` 版本时必须人工复核并跑 `tests/MarketAnalysisWorkflowMafContractTest.cs`：

| 依赖点 | 位置 | 失效表象 | 护栏 |
|--------|------|---------|------|
| AIAgent 两阶段调用（ChatMessage 转发 → TurnToken 真实执行），进度只统计 TurnToken 阶段 | `MarketAnalysisWorkflow.ExecuteWorkflowAsync` 的 `executingAnalysts` | 进度条瞬间冲满 / 分析师永不标记完成（无异常） | 契约测试（进度不变量断言） |
| ExecutorId 由 ASCII Name 经 `GetDescriptiveId` 派生，名称→显示名按前缀回查 | `MarketAnalysisWorkflow` 的 `NameToDisplayName` / `GetExecutorNamePrefix` | 显示名错乱、Token 归属"未知" | 契约测试（ExecutorId 前缀断言） |
| `WithOutputFrom` auto-yield 在流式路径的时序缺陷 → Coordinator / 选股 `AnalyzeAssetsExecutor` 显式 `YieldOutputAsync` | `CoordinatorExecutor.HandleAsync`、`AnalyzeAssetsExecutor.HandleAsync` | 报告 / 选股结果丢失（分别抛"未收到输出"/"未返回结果"友好错误） | 报告：契约测试（成功返回报告即守护）；选股：无自动化护栏，升级后跑一次真实选股 |
| 选股进度依赖 `ExecutorInvokedEvent` / `ExecutorCompletedEvent` 的 ExecutorId 以 Executor 名为前缀 | `InvestmentSelectionWorkflow.ToStep` | 三步状态点不推进（无异常，结果仍正常返回） | 无自动化护栏，升级后跑一次真实选股观察三步推进 |
| Fan-In barrier 目标执行器异常不以 `ExecutorFailedEvent` 暴露 + 偶发消息路由丢失 | `AnalysisAggregatorExecutor` 宽限降级 | 事件流无声终止 / 降级发送失效 | `MarketAnalysisDegradationTest` |
| 默认 workflow state scope 跨 Executor 不可见（issue #2162）→ 显式 scope | `AnalysisDispatcherExecutor.QueueStateUpdateAsync` | Coordinator 取不到标的代码，抛错（显性） | 现有构建与降级测试 |
| `Microsoft.Agents.AI.Compaction` 实验性 API（MAAI001） | `MarketAssistant.Agents` 项目 `Middleware/ConversationCompressionMiddleware.cs` 内的 `ConversationCompactionProviderFactory`（`CompactionProvider` + `SummarizationCompactionStrategy`，文件内 `#pragma warning disable MAAI001` 局部抑制；文件名为历史命名，类型是 `AIContextProvider` 工厂而非中间件） | 编译错误（显性） | 编译 |
| `AIAgentBuilder.Use` 仅提供 `runFunc` 时流式为批量结果回放；宿主按 `MessageId` 重建响应；`AgentResponse.Messages` 可原位追加、`Usage` 可原位累加 | `AIAgentToolDrivenOutput.RunAsync` | 收尾 JSON 被并入上一条消息 / 收尾调用用量丢失 / NotSupportedException（需改为构造新 AgentResponse） | `tests/ToolDrivenOutputTest.cs`（宿主重建与用量守恒断言） |

**幂等规避**（框架修复后代码照常工作，无需专项测试）：显式 `YieldOutputAsync`（Coordinator 与选股 `AnalyzeAssetsExecutor` 各一处）、显式 state scope、宽限降级——升级时确认即可，不必改动。

### AI 选股 / 选币工作流（Agents/InvestmentSelection）

三步确定性流程（生成筛选条件 → 执行筛选 → AI 分析）与 `MarketAnalysisWorkflow` 一样经 `RunStreamingAsync` + `WatchStreamAsync` 消费事件；
`InvestmentSelectionService` 的三个入口（需求 / 新闻 / 快速策略）都带可选 `IProgress<InvestmentSelectionProgress>`，不传即无进度。

- **进度是工作流的真实阶段，不是定时器假进度**：`ExecutorInvokedEvent` → 该步 `Running`，`ExecutorCompletedEvent` → `Completed`
  （可带一句结果摘要，如"候选 48 个标的"）。服务层只 `progress?.Report`，不关心线程；UI 侧用 `Progress<T>` 在构造线程（UI）回调。
- **推荐项的现价与涨跌幅是展示快照**：`AnalyzeAssetsExecutor` 校验通过后把筛选快照的 `Current` / `Pct` 回填到
  `InvestmentRecommendation.CurrentPrice` / `ChangePercent`，两者带 `[JsonIgnore]`，**不属于 LLM 输出契约**，不要让模型生成它们。
- **失败与"0 推荐"必须可区分**：事件流未产出结果抛 `FriendlyException`（"未返回结果"），不要伪装成默认的 0 推荐结果；
  取消靠 `CancellationToken` 贯穿到模型调用，UI 取消后丢弃结果。

### 预发布依赖升级关注点

版本集中在 `Directory.Packages.props`，由 `MarketAssistant.Rag` 与本项目共同引用。预发布包的 API 与落盘格式在 GA 前可能变化，升级时按下表复核（`Markdown.Avalonia` `12.0.0-a3` 也是预发布，属 UI 渲染依赖，随 Avalonia 升级时一并复核聊天与报告的 Markdown 渲染）。

| 包 | 版本 | 使用点 | 升级时关注 |
|----|------|--------|-----------|
| `Microsoft.SemanticKernel.Connectors.SqliteVec` | `1.74.0-preview` | `Services/ServiceCollectionExtensions.cs` 的 `AddSqliteVectorStore`（库文件 `%APPDATA%/{AppName}/vector.sqlite`）；记录模型 `Rag/TextParagraph.cs`（`[VectorStoreKey/Data/Vector]` 特性），由 `RetrievalOrchestrator`、`RagIngestionService`、`DocumentVectorizationService` 读写 | 特性名、`VectorStore` 抽象与落盘表结构都可能变。升级前备份 `vector.sqlite`，升级后用既有库跑一次入库 + 检索；不兼容时需重建向量库（文档重新导入） |
| `Microsoft.SemanticKernel.Plugins.Web` | `1.77.0-alpha` | `Infrastructure/Factories/WebSearchService.cs`（Bing / Brave / Tavily 的 `TextSearch`） | 命名空间与 `ITextSearch<TRecord>` 泛型 API 可能调整（编译期显性）。升级后三家提供商各做一次真实搜索，确认 `TextSearchResult` 的 `Name` / `Link` / `Value` 映射仍完整——`GroundingSearchTools` 的结果渲染与外部数据围栏依赖它 |

### 分析师产物在汇聚点规整一次

`AnalysisAggregatorExecutor` 是分析师文本进入下游的**唯一边界**：失败标记原样保留（原因里可能含花括号），其余消息经 `LlmJsonExtractor.TryExtractJson` 规整为首个结果 JSON 再入库 / 转发，取不到 JSON 才保留原文。模型在 JSON 之外加的说明文字、代码围栏、重复输出的第二份，在这里就丢掉，下游（`CoordinatorExecutor` 的反序列化、`AnalystSignalParser`、报告的 `AnalystMessages`、各导出通道）拿到的都是同一份干净结果，不要在各消费方再各自去重 / 清洗——此前正是只有 Markdown 导出把 JSON 之外的残留原样打了出来，才出现「卡片只有一份、导出里重复」的不一致。回归测试：`AnalysisAggregatorExecutorTest.Collect_AnalystTextWithNoise_ShouldStoreOnlyResultJson`、`Collect_FailureMarkerWithBraces_ShouldStayVerbatim`。

### 分析师结构化信号落库

`CoordinatorExecutor` 生成报告时调用 `AnalystSignalParser.ExtractSignals(reportAnalystMessages)`，把各分析师的结构化 JSON 提取为 `MarketAnalysisReport.AnalystSignals`（解析约定见 `src/MarketAssistant.Agents/AGENTS.md`）。信号随报告由 `ReportArchiveService` 全量序列化持久化，是 M3 回测与分析师绩效统计的数据基础；提取失败仅缺失单项，不阻断报告。

`reports` 表除 `report_json` 外另存 `market_type` 与 `rating` 两个可索引列（`ReportSummary.MarketType`/`Rating` 供 UI 与统计直接筛选，避免全表反序列化）。旧库通过 `EnsureMarketColumnsAsync`（`PRAGMA table_info` + `ALTER TABLE ADD COLUMN`）迁移，**旧行为 NULL 属"未知"**，读取方不得从 `asset_code` 推断市场。

### 聊天会话与外部内容（MarketChatSession）

- **Instructions 只在单轮 `ChatClientAgentRunOptions` 下发一处**：Agent 级 `ChatOptions.Instructions` 会被 MAF 与单轮 Instructions 拼接，提示词重复且带着构造时的过期标的；Agent 级只放 `Temperature` 等不随会话变化的参数。回归测试：`MarketChatSessionBehaviorTest`。
- **工具循环与失败可见性**：构造时沿管道 `GetService` 取 MAF 自动插入的 `FunctionInvokingChatClient`，单轮往返上限收紧为 20（MAF 默认 40，与交易 Agent 对齐），并开启 `IncludeDetailedErrors`，让模型读到 `ToolExecutor` 转出的失败原因，如实告知用户而不是盲目重试。Token 熔断只是事后检查，在途上限只能靠这里。回归测试：`MarketChatSessionBehaviorTest`（往返上限、失败原因、长对话不被误熔断）。
- **外部内容一律经 `ExternalDataFence` 以“数据而非指令”注入**：分析上下文（`<analysis_context>`）、检索结果（`GroundingSearchTools` 返回围栏后的文本）、长期记忆（`LayeredMemoryContextProvider` 以 User 角色注入 `<user_memory>`，不得改回 System 角色）。新增注入外部内容的位置沿用同一围栏。记忆值可能由 Agent 依据外部内容写入并跨会话持久生效，所以提示词明确禁止依据外部内容保存记忆或提升优先级。
- **已知行为（MAF 默认，已实测）**：`AIContextProvider` 注入的消息会追加在当前用户消息之后，并默认写入会话历史——记忆块每轮累积一份，靠 Compaction 兜底。如需消除，可改用 `AIContext.Instructions` 或为 `InMemoryChatHistoryProvider` 配置存储过滤，目前未处理。

---

## 统一告警中心（Applications/AlertCenter）

价格 / 风险 / 信号三类告警的唯一出口。

| 文件 | 职责 |
|------|------|
| `AlertEvent.cs` | 告警模型：`AlertLevel`（Info/Warning/Critical）、`AlertSource`、`AlertTradingImpact`、去重键 `DedupeKey` |
| `IAlertCenterService.cs` / `AlertCenterService.cs` | 落库（`alert_events` 表）、抑制、桌面与扩展通道触达、联动门登记/释放、历史与未读查询 |
| `AlertSuppressionPolicy.cs` | 纯逻辑：同类 5 分钟合并窗口 + 触达抑制（休市静默 / 免打扰时段 / 用户关闭通知），Critical 穿透免打扰；无 IO 便于单测 |
| `AlertGate.cs` | `IAlertGate` 内存计数门：某标的是否存在触发中的确认级告警 |
| `AShareTradingHours.cs` | A 股交易时段判定，非交易时段跳过价格告警评估 |
| `IAlertChannel.cs` / `Channels/FeishuAlertChannel.cs` | 进程外触达通道（飞书自定义机器人）：只投递已通过抑制判定的告警，按各自 `MinimumLevel` 过滤 |
| `Channels/FeishuWebhookClient.cs` / `FeishuWebhookMessage.cs` | 飞书 webhook 客户端与签名/报文纯计算（命名 HttpClient "Feishu"，5s 超时 + 1 次重试） |

新增告警来源时的约定：

- 一律通过 `IAlertCenterService.RaiseAlertAsync(AlertEvent)` 上报，**不要**在业务服务里直接调用 `INotificationService` 弹告警窗（触达由告警中心统一按级别、免打扰时段与抑制策略分发到桌面与各扩展通道）。
- 需要"告警期间该标的 AI 交易强制人工确认"时，把 `AlertEvent.TradingImpact` 置为 `RequireConfirmation`；条件解除后必须调用 `RaiseAlertClearedAsync(source, marketType, symbol, title)` 释放联动门，否则该标的会持续被门控。**标题必须是稳定字符串**——联动门按 (来源, 市场, 标的, 标题) 登记与释放。
- 保护性平仓（`requireClose`）不受联动门影响，确保告警期间仍能及时离场。
- 抑制状态为内存态，重启清零可接受；持久化只存告警记录本身。
- 通道实现必须自行隔离异常：单通道失败只记日志，不得影响告警主链与其它通道。

### 告警规则维度（Applications/PriceAlert）

规则条件分两族，评估链路不同：

- **价格 / 涨跌幅**：随行情 tick 评估（Crypto 走 WS、A 股走 20s 轮询）。
- **指标类**（量比、MACD/RSI/KDJ/BOLL/MA 交叉与突破）：由 `IndicatorAlertEvaluator` 每 5 分钟周期评估，经 keyed `ITechnicalIndicatorService` + `IKLineService` 取数；指标公式一律复用 `TechnicalIndicatorService`（`CalculateFrom` 允许传入已取到的 K 线，避免重复请求），**不得在规则层重写公式**。
- `AlertRuleSymbolResolver`：`Scope=Watchlist` 的批量规则按市场展开自选股（30s TTL 缓存 + 冷缓存预热）；自选股查询失败向外抛，由调用方按规则隔离，**不得降级为空列表**（那会让批量规则静默失效）。
- 批量规则的每个符号持有独立运行时副本（`PriceAlertRule.CloneForSymbol`）：去抖/冷却/触发计数跨符号隔离，否则会互相污染。
- 指标度量样本不足（值为 null）时该符号本轮跳过，不得伪装成"条件未满足"。
- `AnalysisRatingAlertService`：报告归档后与同一标的的上一次评级比对，变化即上报（下调 Warning / 上调 Info），可在设置中关闭。

---

## 自主交易模块成熟化约定（Trading）

在既有资金安全链（风控 → 确认 → symbol 锁 → 客户端快照 → 重试 → 记录）之上，遵循以下约定：

- **紧急停止（kill switch）是总闸**：`EmergencyStopService` 提供两条独立路径——「停止策略并撤单」与「停止并全部平仓」。停机标记持久化在 `trading_control` 表（全局、不随环境分键），`RiskManager` 拒单 + `MarketMonitor` 拒绝启动双重强制执行；解除停机不自动恢复策略（由用户逐个启用）。
- **紧急平仓绕过风控与确认，但不绕过安全机制**：仍走 `TradeExecutor` 的客户端快照、symbol 锁、合约开平仓判定、网络重试、成交记录与持仓回写。
- **合约强平距离是一等风险指标**：`LiquidationRiskEvaluator` 周期评估 `|标记价-强平价|/标记价`，Critical 级登记联动门（阻止该标的自动加仓），距离恢复后必须用**固定标题**调用 `RaiseAlertClearedAsync` 释放。
- **交易所侧保护优先**：退出型策略（含 AI 策略首次成交后的固定护栏）挂 reduceOnly 条件单；双腿用 `MC`+策略ID+`S`/`T` 区分，任一腿成交即撤另一条腿（OCO）。`HasLiveOrder` 为 true 时客户端评估跳过，防止双触发。
- **执行质量**：策略限价单超时由 `PendingOrderTimeoutService` 撤单告警（手动单不处理）；市价单成交后计算滑点，超阈值告警（市价单无法事前限滑点）；部分成交即告警（"触发≠完成"），超过 `LimitOrderTimeoutSeconds` 仍未成交的余量由 `OrderStateSyncService.CancelRemainingIfExpiredAsync` 撤掉并告警实际成交量（`0` 表示不自动撤；撤单失败下轮重试，撤单后状态以交易所回填为准）。
- **风控分层**：账户级（既有）+ 单策略仓位上限（`ValidateOrderAsync` 的 `strategyId` 参数，与账户级取更严者）+ 连续亏损熔断（`daily_stats.consecutive_losses`，只计已实现盈亏）+ 合约名义总敞口上限（Σ|持仓名义|/权益，现货仓位占比无法反映杠杆风险）+ API 错误率熔断（`ApiHealthMonitor` 在 `RoutingExchangeClient` 统一埋点，5 分钟滑窗）+ 同时持仓标的数上限（`MaxConcurrentPositions`，相关性风险的落地代理）+ 资金费率护栏（`FundingRateGuardMinutes` / `FundingRateTolerancePercent`，临结算且费率对本方向不利时推迟开仓）。
- **风控软约束的 fail-open 边界（有意决策）**：同时持仓标的数上限与资金费率护栏属「额外收紧」的择时/相关性约束，其辅助查询（交易所持仓列表、资金费率）失败时记日志后**跳过该项校验**——软约束不应因数据源抖动挡掉全部开仓，这与「禁止吞异常返回空集合」的字面冲突在此处按白名单豁免。资金安全链保持 fail-closed 不变：杠杆设置失败、现货卖单余额校验、合约开平仓判定、实盘准入闸门（`RequireLivePromotion`，含自动策略 ID 查无对应策略时按未通过处理）失败一律拒单。

### 交易模块不可回退的资金正确性约定

以下每条都对应一个曾经存在、且会静默亏钱的缺陷。改动交易链路时必须保持：

- **交易 Agent 只读，写操作不暴露给模型**：`TradingAgent` 只持有账户余额/持仓/订单状态、策略查询与行情指标工具
  （`CryptoTradingExecutionTools` / `CryptoStrategyTools` 不含下单、撤单、改策略状态、建护栏）。
  下单只经 `AISignalStrategyExecutor` 解析 JSON 决策后走 `TradeExecutor`（置信度门控、仓位上限、
  方向校验、风控），否则模型一次工具调用就能绕过这些校验。**不要把写工具加回 Agent**；
  回归测试：`TradingAgentFactoryTest.TradingAgentTools_ShouldBeReadOnly`。
- **AI 决策方向必须驱动下单方向**：`AISignalStrategyExecutor.ExecuteDecisionAsync` 用
  `decision.IsBuy ? Buy : Sell` 决定 `entrySide`，不得退回固定 `strategy.Side`
  （那会让 AI 的 SELL 决策只进日志）。现货 SELL 走 `requireClose: true`，
  无持仓时由 `TradeExecutor` 拒绝，不能变成凭空做空。
  方向驱动后，**硬性边界与影子退出的持仓方向必须从本地账本解析**
  （`ResolveIsLongPositionAsync` / `RecordShadowExitAsync` 按方向分列结算），
  不得再用 `strategy.Side`——合约下 AI 反向开空后 Side 已不代表持仓方向。
- **护栏不得被二次覆盖**：以下单前该策略自身是否有未平仓持仓区分首次建仓与加仓，
  有则保留原止损止盈价、并把数量对齐到该策略全部未平余量（只写增量会让原有仓位
  失去交易所侧保护）。用新价重算会静默抬高/压低已有仓位的止损。
- **订单类型必须是显式映射**：`BinanceExchangeClient.ToExchangeOrderType` 的 switch
  不可退回 `type.ToString().ToUpperInvariant()`——后者产出 `STOPMARKET`（缺下划线），
  交易所拒单后只在日志留一条 warning，表现为"策略看起来有止损、实际没有"。
  回归测试：`ExchangeOrderTypeMappingTest`。
- **下单幂等由"回查收敛"保证，不依赖交易所去重**：普通下单 `clientOrderId` 每次调用
  随机生成并在提交前作为本地记录主键落库。结果不确定时只用
  `GetOrderByClientOrderIdAsync` 查单，不重发 POST；未决意图阻止同标的再次提交。**不要改成按业务键确定性
  派生**：交易所只对未完结订单按幂等 ID 去重，且同键合法重复下单（手动二连、网格震荡
  再触发）会被误判成重复，拿到上一笔订单的结果。条件单的 `MC` 前缀 ID 除外——
  策略+腿与订单 1:1 映射，确定性 ID 正是对账所依赖的。
- **杠杆设置失败即拒开仓**：`SetLeverageAsync` 失败仍下单会以交易所默认（常 20x）成交，
  而 UI 显示 10x。平仓（reduceOnly）不受此限。
- **持仓真相在交易所**：`PositionReconciliationService` 在监控启动与每 5 分钟以交易所持仓校准
  本地 `positions` 账本；现货卖出校验在 `RiskManager` 里取"本地 FIFO 与交易所余额的更严者"。
  不得只信本地账本（外部手动成交会让它虚高，进而放行超卖）。对账修正必须先取该标的的
  `TradeExecutor.GetExecutionGate`（两阶段：批量对比找漂移 → 锁内用最新数据复核再写），
  不加锁会在"读到旧快照 → 交易落库 → 覆盖回旧值"的竞态下清掉新成交。
- **现货也要有交易所侧止损**：现货用 `StopLoss` / `TakeProfit`（触发即市价，
  `BuildConditionOrderSpec(isFutures: false)`），`ExchangeConditionOrderService` 不得在现货提前返回。
  现货 AI 双腿必须打包为**单个 OCO**（`/api/v3/orderList/oco`，撤销任一成员单即撤销整个列表）：
  现货挂单锁定基础资产，两张独立全量卖单的第二张必然因余额被锁而拒单，止盈腿将永远挂不出去。
  AI 策略的护栏腿只在**确有该策略自己的持仓**时才挂（对账里的持仓门控）——持仓消失后
  补挂会生成无持仓保护的裸卖单。现货无 `TRAILING_STOP_MARKET`，追踪止损返回 error 由客户端评估兜底。
- **实盘准入闸门默认关闭但必须可用**：`RiskConfig.RequireLivePromotion` 开启后，
  AI 信号策略需过 `LivePromotionGate`（≥30 笔影子平仓、累计盈亏转正、盈亏比 ≥1.1、
  回撤不超过累计盈利）才允许实盘开仓。该闸门**不是盈利保证**，只阻止未验证策略花真钱；
  退出单（`isExitOrder`）一律豁免，止损离场不能被准入规则挡住。
  定投/网格暂不纳入（影子模式目前仅 AI 具备，纳入只会让它们永久拦单），
  具备影子路径后再扩展 `LivePromotionGate.RequiresPromotion`。
- **条件单腿的成交回写只认腿类型**：`MarketMonitor.OnTradeRecordFilled` 只对
  `StopMarket/TakeProfitMarket/TrailingStopMarket/StopLoss/TakeProfit` 类型的成交走
  "完结保护策略 + 撤兄弟腿"流程；maker 进场挂单晚成交、网格/定投限价单成交等
  普通成交误入会把在场策略错误完结。
- **风控默认值不得静默收紧存量用户**：`RequireLivePromotion`、`FundingRateGuardMinutes`、
  `MaxConcurrentPositions` 一律默认关闭（0/false）——新增的收紧性约束由用户显式开启，
  与"不由程序代替用户决定默认值"的准入闸门同一原则。
- **影子模式必须记账**：`ShadowTradingService` 按与实盘一致口径（FIFO + 手续费）记录模拟成交，影子策略的任何路径（含硬性边界）都不得触达交易所；`StrategyPerformanceService` 输出胜率/盈亏比/最大回撤，影子与实盘同口径可对比。
- 新增 `RiskConfig` 字段直接加属性即可（JSON 持久化，无迁移）；新增表/列走 `TradingSchemaInitializer` 的 `EnsureColumnAsync` 与 `TableDefinitions`，并用 `TradingDataServiceMigrationTest` 覆盖。

---

## 构建

```bash
dotnet build src/MarketAssistant.App.Services/MarketAssistant.App.Services.csproj -c Debug
```
