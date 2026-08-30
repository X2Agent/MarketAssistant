# 统一告警中心（AlertCenter）重构方案

> 状态：待审查。审查通过后再择机实施，本文档仅作设计留存，未做任何代码改动。

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
- `AlertDedupeState`：内存态去抖/冷却状态，不落库，重启重置可接受。

## 三、AlertCenterService

- 接口 `IAlertCenterService`：`RaiseAlertAsync(AlertEvent)`、`AlertRaised` 事件、历史/未读查询、标记已读；SQLite 持久化 `AlertEvent`（复用 `SqliteServiceBase`）。
- 抑制逻辑：
  - 冷却期内同类告警合并；
  - 全局每小时配额（默认 20 条），超限只落库不弹窗；Critical 不受限，但同类 5 分钟内合并为一条"发生 N 次"；
  - 静默窗口：A 股非交易时段不评估价格类告警（复用现有交易时段判断），Critical 例外。
- 触达：`AlertRaised` → `INotificationService` 弹窗；新增 `AlertCenterPageView` + ViewModel 呈现历史。
- 用户偏好加入 `UserSetting`（免打扰时段、每小时配额），`SettingsPageViewModel` 增配置项。

## 四、迁移现有价格告警

- `PriceAlertService` 触发出口改为调用 `IAlertCenterService.RaiseAlertAsync`，不再直接弹通知；轮询/WS 评估逻辑不动。
- `UpdateTriggerState` 内实现确认期 / 冷却期 / 限次判定。

## 五、风险告警评估器（RiskAlertEvaluator）

- 挂接在 `MarketMonitor` 价格消费管线中，与 AI 信号评估同级，**不侵入 `RiskManager`**。
- 触发项：
  - 持仓回撤达到熔断阈值的一定比例（如 80%）→ Warning；
  - AI 信号被风控 Reject / RequireConfirmation → Warning；
  - `BinanceWebSocketService` 断线 / 数据源异常 → Critical，重连恢复后发 Info。

## 六、确认级交易联动（IAlertGate）

- 新增轻量 DI 单例 `IAlertGate`：`IsGated(MarketType, symbol)` —— 该标的是否存在 `TradingImpact=RequireConfirmation` 且触发中的告警。
- `TradeExecutor` 在风控校验后、确认环节前查询 `IAlertGate`：命中则强制走 `ConfirmationCallback` 人工确认（即使风控结果为 Pass），弹窗文案说明"因告警触发需确认"。
- 原则：**告警事件 ≠ 交易指令**，不自动下单、不修改 `RiskManager`。

## 七、接线

- DI：`AddBusinessServices()` 注册 `IAlertCenterService`、`IAlertGate` 及各评估器；`App.axaml.cs` 启动初始化（与现有 PriceAlertService 同模式）。
- 分层依赖保持在 App.Services 内部，不新增跨层依赖。

## 八、验证与实施顺序

- `dotnet build MarketAssistant.slnx -c Debug` 通过。
- 可选：为触发判定（去抖/冷却/限次）与配额/合并逻辑补单元测试。

实施顺序：模型与 AlertCenterService → 通知/历史页 → 价格告警迁移 → 风险评估器 → IAlertGate 交易联动 → 设置项 → 构建验证。
