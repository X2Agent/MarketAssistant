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
- 对外部 HTTP/API 的直接调用优先收敛到 `MarketAssistant.DataProviders` 或已存在的统一服务中，不在多个 Tool/Service 内重复实现。

### 「空」与「失败」必须可区分（强制约定）

数据服务禁止把异常吞成空集合或占位对象——那会让"数据源不可用"在 UI 上伪装成"没有数据"。

- 返回空集合 / `null` / 默认占位对象，语义限定为**数据源正常但查无数据**。
- 数据源故障、解析失败、超时等一律向外抛出。需要保留取消语义时先 `catch (OperationCanceledException) { throw; }` 再包装其余异常为 `FriendlyException`（携带原始异常）。
- 批量接口（如 `IAssetInfoService.GetAssetInfosAsync`）按项隔离：单项失败占位为 `null`，不得让整批失败。
- 编排层（`Applications/`）负责把异常翻译成 UI 状态，UI 层不再二次判断。

---

## 市场行情区块服务（Applications/Assets）

详情页信息密度的数据来源，均为 **Keyed Services**（按 `MarketType` 注册，A 股与 Crypto 各一套实现）。

| 接口 | 职责 | 注册 |
|------|------|------|
| `IMarketSnapshotService` | 行情速览（今开/最高/最低/昨收/量额/换手/振幅/量比/PE/PB） | `AShareMarketSnapshotService` / `CryptoMarketSnapshotService` |
| `ITechnicalIndicatorService` | 技术指标快照（MA/MACD/BOLL/KDJ） | 市场无关实现（`TechnicalIndicatorService`，复用 `Skender.Stock.Indicators`），数据不足时返回 `null` 而非抛异常 |
| `IOrderBookService` | 盘口深度 | Crypto：`CryptoOrderBookService`（币安深度）；A 股：`AShareOrderBookService`（新浪个股行情五档）。A 股解析器带交叉校验，任一条不满足即返回 `null` |
| `ITickTradeService` | 逐笔成交 | Crypto：`CryptoTickTradeService`；A 股：`AShareTickTradeService`。**逐笔不含主动买卖方向**，UI 不得据 `IsBuyerMaker` 推断内外盘 |

> 两个市场的 `SupportsOrderBook` / `SupportsTickTrades` 能力位目前**均为 `true`**，
> `NoopOrderBookService` / `NoopTickTradeService` 仅作为新增市场缺少数据源时的兜底契约位保留。

- 新增市场区块服务时，必须在 `MarketContext` 对应的 MarketModule 中注册，并同步更新 `IMarketCapability` 的能力位。每个市场都必须注册全部 keyed 接口——不支持的市场注册 `Noop*` 实现（返回空集合），**不要**抛异常伪装成加载失败，也不要在业务层用 `if/else` 绕开解析。
- 不支持某能力的市场由页面显式说明数据源限制；区块之间必须可独立加载与独立失败，禁止把多个区块串成一个必须全部成功的事务。

---

## 每日 AI 简报（Applications/Briefing）

自选股的"要闻速览 + 异动归因"摘要，按市场 Keyed 注册（`IDailyBriefingService`），SQLite 表 `briefings` 按 `(market_type, date_key)` 唯一，当天生成后各端复用。**纯手动生成**：构造函数只读当天缓存，绝不自动触发模型调用。

- **重要性与相关性判断交给模型**：代码只做条数截断，**不做本地重要性/相关性过滤**。实测 A 股同花顺 `import` 字段 97% 为 0（无区分度）、88% 的快讯无关联股票（`stock` 为空或外盘），基于这些信号硬过滤等于用噪声决策。`Telegram.Symbols` 在 A 股因字段大小写不匹配恒为空，不可用。
- **要闻覆盖约 28~29 小时**：数据源 `pagesize=100`（A股）/`take=100`（币圈）实测跨度 28.9h / 28.3h；原先 `20` 条仅覆盖 2.1h / 9.2h，缺失隔夜与开盘前消息。
- **Token 预算硬边界**：`DailyBriefingService` 的 `MaxFavorites/MaxNewsItems/MaxNewsTitleLength/MaxNewsContentLength/MaxBriefingLength/MaxOutputTokens` 常量钳制输入输出；自选按异动幅度优先纳入，要闻按数据源时间倒序取前 N 条。调大任何边界前先评估成本（要闻部分当前约 2100 token/次，为早期 8 条方案的约 5.5 倍）。
- **单次调用、无重试链**：经 `IChatClientFactory.CreateRuntime()` 默认模型一次 `GetResponseAsync` 完成；失败直接抛给调用方（UI 展示重试），不自动重试烧 Token，失败结果不落盘。
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
- runtime 缓存按指纹区分，`MaxCachedRuntimes` 为上限；新增角色模型组合会占用缓存槽位，调整上限时注意内存占用。

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
- 通用知识型 Skill 资源保留在 `MarketAssistant.App/skills/` 作为内容文件输出，本项目通过 `FileAgentSkillsProvider` 加载，不在这里重复存放一份。

### 分析师结构化信号落库

`CoordinatorExecutor` 生成报告时调用 `AnalystSignalParser.ExtractSignals(reportAnalystMessages)`，把各分析师的结构化 JSON 提取为 `MarketAnalysisReport.AnalystSignals`（解析约定见 `src/MarketAssistant.Agents/AGENTS.md`）。信号随报告由 `ReportArchiveService` 全量序列化持久化，是 M3 回测与分析师绩效统计的数据基础；提取失败仅缺失单项，不阻断报告。

`reports` 表除 `report_json` 外另存 `market_type` 与 `rating` 两个可索引列（`ReportSummary.MarketType`/`Rating` 供 UI 与统计直接筛选，避免全表反序列化）。旧库通过 `EnsureMarketColumnsAsync`（`PRAGMA table_info` + `ALTER TABLE ADD COLUMN`）迁移，**旧行为 NULL 属"未知"**，读取方不得从 `asset_code` 推断市场。

---

## 统一告警中心（Applications/AlertCenter）

价格 / 风险 / 信号三类告警的唯一出口。

| 文件 | 职责 |
|------|------|
| `AlertEvent.cs` | 告警模型：`AlertLevel`（Info/Warning/Critical）、`AlertSource`、`AlertTradingImpact`、去重键 `DedupeKey` |
| `IAlertCenterService.cs` / `AlertCenterService.cs` | 落库（`alert_events` 表）、抑制、弹窗触达、联动门登记/释放、历史与未读查询 |
| `AlertSuppressionPolicy.cs` | 纯逻辑：同类 5 分钟合并窗口 + 全局每小时配额（Critical 不受限），无 IO 便于单测 |
| `AlertGate.cs` | `IAlertGate` 内存计数门：某标的是否存在触发中的确认级告警 |
| `AShareTradingHours.cs` | A 股交易时段判定，非交易时段跳过价格告警评估 |

新增告警来源时的约定：

- 一律通过 `IAlertCenterService.RaiseAlertAsync(AlertEvent)` 上报，**不要**在业务服务里直接调用 `INotificationService` 弹告警窗（弹窗由告警中心统一按级别、免打扰时段与配额分发）。
- 需要"告警期间该标的 AI 交易强制人工确认"时，把 `AlertEvent.TradingImpact` 置为 `RequireConfirmation`；条件解除后必须调用 `RaiseAlertClearedAsync(source, marketType, symbol, title)` 释放联动门，否则该标的会持续被门控。
- 保护性平仓（`requireClose`）不受联动门影响，确保告警期间仍能及时离场。
- 抑制状态为内存态，重启清零可接受；持久化只存告警记录本身。

---

## 构建

```bash
dotnet build src/MarketAssistant.App.Services/MarketAssistant.App.Services.csproj -c Debug
```