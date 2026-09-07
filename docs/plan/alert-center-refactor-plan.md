# 统一告警中心（AlertCenter）重构方案

> 状态：已在分支 `refactor/alert-center` 实施。价格告警去抖/冷却/限次、告警落库与历史页、
> 未读徽标、风险与行情连接告警、确认级交易联动门（`IAlertGate`）均已接入，
> 单元测试见 `tests/Application/AlertCenterTest.cs`、`AlertSuppressionPolicyTest.cs`、
> `AlertGateTest.cs`、`PriceAlertRuleAdvancedTest.cs`。

## 背景与现状

项目已有一套完整的价格预警闭环：

- `PriceAlertRule`（`src/MarketAssistant.App.Services/Applications/PriceAlert/PriceAlertRule.cs`）：条件判定、`IsOneTime` 一次性触发。
- `PriceAlertService`（`src/MarketAssistant.App.Services/Applications/PriceAlert/PriceAlertService.cs`）：SQLite 持久化规则；A 股 `PeriodicTimer` 20s 轮询、crypto 走 `BinanceWebSocketService` 推送评估。
- 通知：`INotificationService` + `NotificationWindow` 右下角弹窗；`App.axaml.cs` 启动接线。
- 交易链路：`MarketMonitor`（价格流）→ `AISignalStrategyExecutor`（AI 信号）→ `TradeExecutor`（风控 → 确认 → 下单 → 记录），`RiskManager.ValidateOrderAsync` 返回 `RiskCheckResult`（Pass / Reject / RequireConfirmation），`TradeExecutor.ConfirmationCallback` 为现成的人工确认钩子。

现有痛点：告警出口分散、只有价格一类、无去抖/冷却/限次机制、告警与交易决策无联动。

## 设计决策（已确认）

- **范围**：统一告警中心，收敛价格 / 风险 / 信号三类告警，现有价格告警迁入。
- **交易联动强度**：默认"确认级"——告警触发中时 AI 交易信号升级为需人工确认，不自动下单。

## 一、告警分类

| 类型 | 来源 | 默认级别 |
|------|------|---------|
| 价格告警 | PriceAlertRule（已有） | Warning |
| 风险告警 | 回撤接近熔断、信号被风控拒绝、WS 断线/数据源异常 | Warning / Critical |
| 信号告警 | AI 信号产出、MarketMonitor 策略状态变化 | Info |

统一产出 `AlertEvent`，UI 提供历史列表 + 未读角标，触达走 `INotificationService`。

## 二、核心模型（新增，`src/MarketAssistant.App.Services/Applications/AlertCenter/`）

- `AlertEvent`：Id、MarketType、Symbol、Level（Info/Warning/Critical）、Source（PriceAlert/Risk/Signal/System）、Title、Content、CreatedAt、IsRead。
- `PriceAlertRule` 扩展：
  - `MaxTriggerCount`（一次性 / 限次 / 不限次，替代 `IsOneTime` 语义，保留兼容）；
  - `ConfirmTicks` / `ConfirmSeconds`（去抖确认期：条件需持续成立才触发）；
  - `CooldownMinutes`（冷却期：触发后 M 分钟内不重复）；
  - `TradingImpact`（枚举：None / RequireConfirmation，本期仅这两种）。
- 去抖/冷却状态：内存态，不落库，重启重置可接受。实现为 `AlertSuppressionPolicy.MergeState`（按去重键）与 `QuotaState`（全局配额窗口），未采用原计划的单一 `AlertDedupeState` 命名——合并状态按去重键、配额状态全局共享，生命周期不同，拆开后纯逻辑可直接单测。

## 三、AlertCenterService

- 接口 `IAlertCenterService`：`RaiseAlertAsync(AlertEvent)`、`AlertRaised` 事件、历史/未读查询、标记已读；SQLite 持久化 `AlertEvent`（复用 `SqliteServiceBase`）。
- 抑制逻辑：
  - 冷却期内同类告警合并；
  - 全局每小时配额（默认 20 条），超限只落库不弹窗；Critical 不受限，但同类 5 分钟内合并为一条"发生 N 次"；
  - 静默窗口：A 股非交易时段的价格类告警只落库不弹窗，**Critical 例外**（确认级告警需即时触达并驱动交易联动门）。判定落在告警中心抑制层（`AlertSuppressionPolicy.IsSilencedByTradingSession`）；轮询层休市时仅继续评估确认级规则，普通规则跳过以省去无效行情请求。注：项目内原无可复用的交易时段判断（`IsMarketOpen` / `IsTradingTime` 全库无实现），故新建 `AShareTradingHours`（东八区 09:30–11:30 / 13:00–15:00，含 Windows / IANA 时区 ID 回退）。
- 触达：`AlertRaised` → `INotificationService` 弹窗；历史页实现为 `AlertHistoryView` + `AlertHistoryViewModel`，作为"价格预警"页内的"告警历史"标签呈现（未采用原计划的独立 `AlertCenterPageView` 导航页——三类告警共用一份历史即可，独立页会稀释规则入口）；主窗口导航以未读徽标跨类提示。
- 用户偏好加入 `UserSetting`（免打扰时段、每小时配额），`SettingsPageViewModel` 增配置项。

## 四、迁移现有价格告警

- `PriceAlertService` 触发出口改为调用 `IAlertCenterService.RaiseAlertAsync`，不再直接弹通知；轮询/WS 评估逻辑不动。
- `UpdateTriggerState` 内实现确认期 / 冷却期 / 限次判定。

## 五、风险告警评估器（RiskAlertEvaluator）

- 挂接在 `MarketMonitor` 价格消费管线中，与 AI 信号评估同级，**不侵入 `RiskManager`**。
- 触发项：
  - 持仓回撤达到熔断阈值的一定比例（如 80%）→ Warning；
  - 策略触发被风控 Reject / RequireConfirmation 拒绝 → Warning（`Source=Risk`）；用户主动拒绝或人工确认超时改由信号告警覆盖（`Source=Signal`），二者按 `TradeRejectionReason.IsUserRejection` 互斥分流，避免同一事件双告警；
  - `BinanceWebSocketService` 断线 / 数据源异常 → Critical，重连恢复后发 Info。
- 信号类告警由同层的 `SignalAlertEvaluator`（`AlertSource.Signal`）产出：策略自动暂停（用户拒绝路径，Warning）、策略完结（Info）、AI 信号成交（Info）。`MarketMonitor` 因此不再持有 `INotificationService`，最后一处直连弹窗（策略自动暂停提示）已收敛到告警中心。

## 六、确认级交易联动（IAlertGate）

- 新增轻量 DI 单例 `IAlertGate`：`IsGated(MarketType, symbol)` —— 该标的是否存在 `TradingImpact=RequireConfirmation` 且触发中的告警。
- `TradeExecutor` 在风控校验后、确认环节前查询 `IAlertGate`：命中则强制走 `ConfirmationCallback` 人工确认（即使风控结果为 Pass），弹窗文案说明"因告警触发需确认"；无确认订阅者时拒绝下单而非放行。
- 实现补充（原计划未列）：
  - **保护性平仓豁免**：`requireClose = true`（止损 / 止盈 / 追踪止损等退出型触发）不受门限制，避免"因告警反而无法及时离场"这一更危险的结果；
  - **门判定当前仅覆盖 Crypto**：`TradeExecutor` 本身是 Crypto keyed 服务，A 股侧尚无自动交易链路，A 股告警门仅用于 UI 呈现。
- 原则：**告警事件 ≠ 交易指令**，不自动下单、不修改 `RiskManager`。

## 七、接线

- DI：`AddBusinessServices()` 注册 `IAlertCenterService`、`IAlertGate` 及各评估器；`App.axaml.cs` 启动初始化（与现有 PriceAlertService 同模式）。
- 分层依赖保持在 App.Services 内部，不新增跨层依赖。

## 八、验证与实施顺序

- `dotnet build MarketAssistant.slnx -c Debug` 通过。
- 单元测试已补齐：触发判定（去抖/确认期/冷却/限次）、抑制策略（合并/配额/免打扰/休市静默）、告警中心集成（落库/合并/未读/联动门）、交易门与交易时段、信号告警分流。`dotnet test --filter TestCategory=Unit` 360/360 通过；`dotnet format --verify-no-changes` 无差异。

实施顺序：模型与 AlertCenterService → 通知/历史页 → 价格告警迁移 → 风险评估器 → IAlertGate 交易联动 → 设置项 → 构建验证。

## 九、实现取舍与遗留

- **`TradingImpact` 与 `Level` 耦合**：价格告警中 `TradingImpact = RequireConfirmation` 映射为 `AlertLevel.Critical`（`PriceAlertService.RaiseAlertSafeAsync`），因此确认级规则天然绕过每小时配额、免打扰时段与休市静默。取舍是"确认级必达"优先于"弹窗噪声"；若噪声过大可改为两维解耦（级别由规则单独配置）。
- **门与告警状态均为内存态**：重启后 `AlertGate` 清空，同时 `PriceAlertService.LoadRulesAsync` 将规则 `Triggered` 重置为 false，两者一致，不会出现"规则显示已触发但门未生效"的残留。
- **信号告警覆盖面**：当前仅上报策略自动暂停（用户拒绝路径）、策略完结、AI 信号成交三类；策略手动启停、网格/DCA 常规成交暂不上报，按实际噪声反馈再决定是否纳入。
- **遗留死代码**：`PriceAlertRule.AlertModeText`（"一次性/持续"）全库无引用（视图使用内联 `IsOneTime` 标签），待小改动清理。
- **静默告警仍消耗每小时配额**：配额在 `AlertSuppressionPolicy.Evaluate` 内递增，而休市/免打扰抑制在其外部判定，因此被压掉弹窗的告警仍计入配额。未修——修正需把抑制判定并入 policy（涉及将 `AShareTradingHours` 注入纯逻辑层的可测性设计）；若实测夜间噪声明显挤占白天配额再处理。

## 十、审计后补修（第二轮）

对首轮实现逐项审计，发现并修复 3 个计划外缺陷：

- **联动门泄漏**：`PriceAlertService.RemoveRuleAsync` 删除规则时未释放确认级联动门（禁用路径已释放），导致删除"触发中的确认级规则"后该标的 AI 信号被永久强制人工确认，仅重启可解。现删除路径同样调用 `RaiseAlertClearedSafeAsync`；Risk/Signal 评估器 `TradingImpact` 恒为 `None`，不经此路径。
- **`ConfirmSeconds` 能力不可达**：模型、建表、持久化、判定、摘要文本齐备，但表单仅有 `ConfirmTicks`。已补"去抖持续（秒）"输入（`PriceAlertPageView.axaml` + `NewRuleConfirmSeconds`）。tick 间隔在 A 股（20s 轮询）与币安（WS 推送）下语义不同，秒维度才是跨市场一致的判定口径。
- **`alert_events` 无界增长**：原实现只有 INSERT/SELECT。新增 30 天保留期，随启动初始化执行一次 `PruneExpiredAlertsAsync`（用 `datetime()` 解析比较，避免 ISO-8601 偏移写法下字符串长度差异导致误判）。

测试：新增 `Initialize_ShouldPruneAlertsBeyondRetentionPeriod`，`dotnet test --filter TestCategory=Unit` 361/361 通过。门泄漏与 `ConfirmSeconds` 属服务装配/表单绑定，`PriceAlertService` 依赖 `BinanceWebSocketService`、`ClsQuoteClient` 具体类且无既有测试替身，未补自动化覆盖，由构建与代码走查保证。

## 十一、审计后补修（第三轮）

对照方案逐项复核代码，修复 3 处门管理缺陷与 1 处展示缺陷：

- **禁用路径联动门泄漏（第二轮误判）**：第二轮称"禁用路径已释放"，实际 `PriceAlertService.ToggleRuleAsync` 禁用规则时同样未释放确认级联动门，仅删除路径已释放。现禁用路径在 `newEnabled == false` 且 `TradingImpact == RequireConfirmation` 时同样调用 `RaiseAlertClearedSafeAsync`，否则禁用触发中的确认级规则后该标的 AI 信号被永久强制人工确认。
- **同标的多条确认级规则门计数泄漏**：`AlertCenterService._activeGatedAlerts` 键原为 `(Source, MarketType, Symbol)`（不含标题），同一标的两条不同条件的确认级规则先后触发→解除时，后触发覆盖先触发，第二次 `RaiseAlertClearedAsync` 的 `Remove` 失败、门计数不归零，标的水久门控。现键扩展为 `(Source, MarketType, Symbol, Title)`，各规则独立登记/释放。
- **一次性确认级规则门永久残留**：一次性规则触发即停用，但原实现 `autoDisabled` 分支先 `RaiseAlertClearedAsync`（门尚未登记，`Remove` 失败）、后 `RaiseAlertAsync`（`Activate` 生效），导致门随规则停用后永不释放。现 `RaiseAlertSafeAsync` 对一次性规则将 `TradingImpact` 降为 `None`（弹窗级别仍为 `Critical`），不再登记门；`autoDisabled` 分支不再释放。
- **规则列表摘要漏显"持续秒"**：`PriceAlertPageViewModel.CreateDisplayRule` 未复制 `ConfirmSeconds`，导致 `RuleOptionsText` 不显示"持续 X 秒"。已补复制。

测试：新增 `RaiseAlert_MultipleConfirmationRulesSameSymbol_ShouldTrackGatesIndependently`，告警相关测试 `dotnet test --filter FullyQualifiedName~Alert` 51/51 通过；`dotnet build MarketAssistant.slnx -c Debug` 0 错误。
