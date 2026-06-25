# 虚拟币自主交易模块设计

> 状态：现货交易主链已实现，并已优先通过 `IExchangeClient` 抽象接入；合约支持和部分策略类型仍在规划中。

## 一、设计目标

独立的虚拟币自主交易助手，支持：

- **混合运行模式**：后台持续监控 + 价格触发分析 + 策略自动执行
- **策略自动化**：用户设置止损/止盈/仓位等参数，助手在限制内自动执行
- **交易平台**：Binance（当前为现货，合约待补充）

---

## 二、架构概览

### 核心组件

当前核心实现主要位于 `src/MarketAssistant.App/Trading/`，包含以下核心组件：

- **MarketMonitor**：后台市场监控，基于 `BinanceWebSocketService` 实时价格流 + `PriceAlertService` 触发条件。
- **TradingAgent**：MAF `ChatClientAgent`，持有交易专用工具集，接收 Monitor 信号后自主分析并决策。
- **StrategyEngine**：用户定义的策略管理（止损/止盈/网格/追踪等），解析策略规则为可执行条件。
- **RiskManager**：风控网关，所有交易指令必须经过风控检查（单笔限额、日限额、最大持仓比例等）。
- **TradeExecutor**：通过 `IExchangeClient` 执行下单并记录交易日志。

### 数据流

```
BinanceWebSocket → MarketMonitor → [触发条件匹配]
                                        ↓
                                  TradingAgent（AI 分析决策）
                                        ↓
                                  StrategyEngine（策略验证）
                                        ↓
                                  RiskManager（风控检查）
                                        ↓
                                  TradeExecutor（执行下单）
                                        ↓
                                  TradingDataService（持久化） + UI 通知
```

### 与现有服务的边界

| 类别 | 服务 | 说明 |
|------|------|------|
| **复用** | `BinanceMarketDataService` | 现货+合约行情数据 |
| **复用** | `BinanceWebSocketService` | 实时价格流（`PriceUpdated` 事件） |
| **复用** | `BinanceAccountService` | Binance 现货 API 底层实现，由 `BinanceExchangeClient` 适配为统一抽象 |
| **复用** | `BinanceAuthService` | HMAC-SHA256 签名，已通过设置服务动态读取密钥 |
| **复用** | `PriceAlertService` | 价格触发逻辑参考 |
| **新建** | `MarketMonitor` | 后台监控 + 策略触发 |
| **新建** | `TradingAgent` | AI 自主决策 |
| **新建** | `StrategyEngine` | 策略管理与条件匹配 |
| **新建** | `RiskManager` | 风控校验 |
| **新建** | `TradeExecutor` | 下单执行 + 日志 |
| **新建** | `TradingDataService` | SQLite 持久化 |

---

## 三、模型定义

### 3.1 枚举类型

```csharp
public enum StrategyType
{
    StopLoss,        // 止损
    TakeProfit,      // 止盈
    TrailingStop,    // 追踪止损
    GridTrading,     // 网格交易
    DCA,             // 定投（Dollar Cost Averaging）
    AISignal         // AI 信号触发（TradingAgent 自主决策）
}

public enum StrategyStatus { Active, Paused, Completed, Failed }
public enum OrderSide { Buy, Sell }
public enum OrderType { Market, Limit }
public enum TradeRecordStatus { Pending, Filled, PartiallyFilled, Cancelled, Failed }
```

### 3.2 交易策略（TradingStrategy）

```csharp
public class TradingStrategy
{
    public string Id { get; set; }
    public string Symbol { get; set; }                 // e.g. "BTCUSDT"
    public StrategyType Type { get; set; }
    public StrategyStatus Status { get; set; }
    public OrderSide Side { get; set; }
    public decimal TriggerPrice { get; set; }          // 触发价格
    public decimal? StopLossPrice { get; set; }        // 止损价
    public decimal? TakeProfitPrice { get; set; }      // 止盈价
    public decimal Quantity { get; set; }               // 交易数量
    public decimal? MaxPositionPercent { get; set; }   // 最大仓位占比 (0-100)
    public string? CustomParams { get; set; }          // JSON：策略特定参数
    public DateTime CreatedAt { get; set; }
    public DateTime? LastTriggeredAt { get; set; }
    public int ExecutionCount { get; set; }            // 已执行次数
    public int? MaxExecutions { get; set; }            // 最大执行次数 (null=无限)
}
```

#### CustomParams 策略特定参数

| 策略类型 | JSON 结构 | 说明 |
|---------|-----------|------|
| TrailingStop | `{ "trailingPercent": 3.0, "activationPrice": 50000 }` | 追踪幅度、激活价格 |
| GridTrading | `{ "upperPrice": 52000, "lowerPrice": 48000, "gridCount": 10, "amountPerGrid": 0.01 }` | 上下界、网格数、每格数量 |
| DCA | `{ "intervalMinutes": 1440, "amountPerBuy": 100 }` | 间隔时间、每次金额（USDT） |
| AISignal | `{ "confidenceThreshold": 0.8, "analysisInterval": 3600 }` | 置信度阈值、分析间隔秒数 |

### 3.3 交易记录（TradeRecord）

```csharp
public class TradeRecord
{
    public string Id { get; set; }
    public string StrategyId { get; set; }             // 关联策略
    public string Symbol { get; set; }
    public OrderSide Side { get; set; }
    public OrderType OrderType { get; set; }
    public decimal RequestedQty { get; set; }          // 请求数量
    public decimal ExecutedQty { get; set; }           // 实际成交数量
    public decimal? RequestedPrice { get; set; }       // 限价单价格
    public decimal ExecutedPrice { get; set; }         // 实际成交均价
    public decimal Commission { get; set; }            // 手续费
    public string CommissionAsset { get; set; }        // 手续费币种
    public TradeRecordStatus Status { get; set; }
    public long BinanceOrderId { get; set; }           // Binance 订单号
    public string? AIReasoning { get; set; }           // AI 决策推理过程
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
```

### 3.4 风控配置（RiskConfig）

```csharp
public class RiskConfig
{
    public decimal MaxSingleOrderPercent { get; set; } = 5;    // 单笔最大仓位 %
    public decimal MaxDailyLossPercent { get; set; } = 10;     // 日最大亏损 %
    public decimal MaxTotalPositionPercent { get; set; } = 80; // 总仓位上限 %
    public int MaxDailyTrades { get; set; } = 20;              // 日最大交易次数
    public decimal MinOrderAmount { get; set; } = 10;          // 最小下单金额 (USDT)
    public bool RequireConfirmation { get; set; } = false;     // 大额单是否需人工确认
    public decimal ConfirmationThreshold { get; set; } = 1000; // 需确认的金额阈值 (USDT)
}
```

### 3.5 日统计（DailyStats）

```csharp
public class DailyStats
{
    public string Date { get; set; }                   // yyyy-MM-dd
    public int TradeCount { get; set; }
    public decimal TotalPnl { get; set; }
    public decimal TotalCommission { get; set; }
}
```

---

## 四、TradingAgent 工具集

### 4.1 工具接口

基于现有 `[RequiresTools]` + Keyed Services 模式设计：

```csharp
/// <summary>
/// 交易执行工具 —— 仅 Crypto 市场
/// </summary>
public interface ITradingExecutionTools
{
    Task<AccountBalanceSummary> GetAccountBalanceAsync();
    Task<List<PositionInfo>> GetCurrentPositionsAsync();
    Task<TradeResult> PlaceOrderAsync(string symbol, OrderSide side,
        OrderType type, decimal quantity, decimal? price = null);
    Task<OrderStatusInfo> GetOrderStatusAsync(string symbol, long orderId);
    Task<bool> CancelOrderAsync(string symbol, long orderId);
}

/// <summary>
/// 策略管理工具 —— TradingAgent 可查询和更新策略状态
/// </summary>
public interface IStrategyTools
{
    Task<List<TradingStrategy>> GetActiveStrategiesAsync();
    Task<TradingStrategy?> GetStrategyAsync(string strategyId);
    Task UpdateStrategyStatusAsync(string strategyId, StrategyStatus status);
}
```

### 4.2 辅助模型

```csharp
public class AccountBalanceSummary
{
    public decimal TotalValueUSDT { get; set; }
    public List<AssetBalance> Assets { get; set; } = [];
}

public class AssetBalance
{
    public string Asset { get; set; } = string.Empty;
    public decimal Free { get; set; }
    public decimal Locked { get; set; }
    public decimal ValueUSDT { get; set; }
}

public class PositionInfo
{
    public string Symbol { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public decimal UnrealizedPnlPercent { get; set; }
}

public class TradeResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public TradeRecord? Record { get; set; }
}

public class OrderStatusInfo
{
    public long OrderId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal ExecutedQty { get; set; }
    public decimal ExecutedPrice { get; set; }
}
```

### 4.3 TradingAgent 声明

```csharp
[RequiresTools(typeof(ITradingExecutionTools))]
[RequiresTools(typeof(IStrategyTools))]
[RequiresTools(typeof(IBasicDataTools))]        // 复用：价格/K线/市值
[RequiresTools(typeof(ITechnicalDataTools))]     // 复用：技术指标
public class TradingAgent : ChatClientAgent { ... }
```

TradingAgent 不继承 `AnalystAgentBase`（它不是分析师），由专用的 `TradingAgentFactory` 创建，使用与 `AnalystAgentFactory` 相同的 `[RequiresTools]` 解析模式。

### 4.4 系统提示词要点

```
角色：虚拟币自主交易助手

能力：
- 查询账户余额和持仓（GetAccountBalance / GetCurrentPositions）
- 分析市场数据：价格、K线、技术指标（复用 IBasicDataTools / ITechnicalDataTools）
- 根据策略规则和风控约束决定是否交易
- 执行买卖操作（PlaceOrder）并记录决策推理

约束：
- 所有交易必须经过 RiskManager 风控检查（PlaceOrder 内部自动调用）
- 单笔不超过账户 {MaxSingleOrderPercent}%
- 日亏损不超过 {MaxDailyLossPercent}%
- 必须记录每次决策的推理过程到 AIReasoning 字段
- 遇到不确定情况，倾向于不交易（宁可错过，不可做错）
```

### 4.5 工具实现位置

| 接口 | 实现类 | 位置 | 依赖 |
|------|--------|------|------|
| `ITradingExecutionTools` | `CryptoTradingExecutionTools` | `src/MarketAssistant.App/Agents/Tools/Crypto/` | `TradeExecutor`, `IExchangeClient` |
| `IStrategyTools` | `CryptoStrategyTools` | `src/MarketAssistant.App/Agents/Tools/Crypto/` | `StrategyEngine` |
| `IBasicDataTools` | `CryptoBasicTools`（已有） | `src/MarketAssistant.App/Agents/Tools/Crypto/` | `BinanceMarketDataService` |
| `ITechnicalDataTools` | `CryptoTechnicalTools`（已有） | `src/MarketAssistant.App/Agents/Tools/Crypto/` | `IKLineService`, `Skender.Stock.Indicators` |

---

## 五、数据持久化方案

### 5.1 SQLite 数据库

数据库路径：`%AppData%/MarketAssistant/trading.db`（与 `reports.db` 同级）

#### 表结构

```sql
CREATE TABLE strategies (
    id TEXT PRIMARY KEY,
    symbol TEXT NOT NULL,
    type INTEGER NOT NULL,
    status INTEGER NOT NULL,
    side INTEGER NOT NULL,
    trigger_price REAL NOT NULL,
    stop_loss_price REAL,
    take_profit_price REAL,
    quantity REAL NOT NULL,
    max_position_percent REAL,
    custom_params TEXT,
    created_at TEXT NOT NULL,
    last_triggered_at TEXT,
    execution_count INTEGER DEFAULT 0,
    max_executions INTEGER
);
CREATE INDEX idx_strategies_symbol ON strategies(symbol);
CREATE INDEX idx_strategies_status ON strategies(status);

CREATE TABLE trade_records (
    id TEXT PRIMARY KEY,
    strategy_id TEXT NOT NULL,
    symbol TEXT NOT NULL,
    side INTEGER NOT NULL,
    order_type INTEGER NOT NULL,
    requested_qty REAL NOT NULL,
    executed_qty REAL NOT NULL,
    requested_price REAL,
    executed_price REAL NOT NULL,
    commission REAL DEFAULT 0,
    commission_asset TEXT,
    status INTEGER NOT NULL,
    binance_order_id INTEGER,
    ai_reasoning TEXT,
    created_at TEXT NOT NULL,
    completed_at TEXT,
    FOREIGN KEY (strategy_id) REFERENCES strategies(id)
);
CREATE INDEX idx_records_strategy ON trade_records(strategy_id);
CREATE INDEX idx_records_symbol ON trade_records(symbol);
CREATE INDEX idx_records_created ON trade_records(created_at);

CREATE TABLE daily_stats (
    date TEXT PRIMARY KEY,
    trade_count INTEGER DEFAULT 0,
    total_pnl REAL DEFAULT 0,
    total_commission REAL DEFAULT 0
);
```

### 5.2 持久化服务（TradingDataService）

```csharp
public class TradingDataService
{
    // 策略 CRUD
    Task SaveStrategyAsync(TradingStrategy strategy);
    Task<TradingStrategy?> GetStrategyAsync(string id);
    Task<List<TradingStrategy>> GetStrategiesByStatusAsync(StrategyStatus status);
    Task UpdateStrategyStatusAsync(string id, StrategyStatus status);
    Task DeleteStrategyAsync(string id);

    // 交易记录
    Task SaveTradeRecordAsync(TradeRecord record);
    Task<List<TradeRecord>> GetTradeRecordsAsync(
        string? symbol, DateTime? from, DateTime? to, int limit = 50);
    Task<List<TradeRecord>> GetRecordsByStrategyAsync(string strategyId);

    // 日统计（风控查询）
    Task<DailyStats> GetTodayStatsAsync();
    Task UpdateDailyStatsAsync(decimal pnl, decimal commission);
}
```

参考 `ReportArchiveService` 的实现模式：使用 `Microsoft.Data.Sqlite`，在构造函数中自动初始化表结构。

### 5.3 风控配置持久化

使用 `Preferences`（与 `UserSettingService` 一致）：

- Key = `"TradingRiskConfig"`
- JSON 序列化 `RiskConfig` 对象
- 默认值在 `RiskConfig` 类中定义

---

## 六、现有服务适配清单

| 服务 | 现状 | 需要补充 |
|------|------|---------|
| `BinanceAccountService` | 已覆盖现货账户、下单、查单、撤单、挂单查询 | 继续评估合约 API 和更细粒度错误映射 |
| `BinanceAuthService` | 完整可用 | 维持通过 `IUserSettingService` 动态读取密钥 |
| `BinanceWebSocketService` | `PriceUpdated` 事件可用 | 无需修改 |
| `PriceAlertService` | 价格触发逻辑可参考 | MarketMonitor 独立实现，但复用相同模式 |

---

## 七、UI 页面规划

- **策略配置页**：创建/编辑/启停策略，设置止损止盈参数，显示策略运行状态
- **交易监控页**：实时显示当前持仓、未完成订单、账户余额概览
- **交易历史页**：查看历史交易记录，按币种/时间筛选，收益统计图表

---

## 八、实施步骤（按优先级）

1. **继续收敛抽象**：保持 `RiskManager`、`TradeExecutor`、Agent 工具与监控链优先依赖 `IExchangeClient`，避免重复封装 Binance 账户逻辑。
2. **扩展交易能力**：补充合约 API、完善订单错误映射与可观测性。
3. **补齐策略类型**：逐步实现 `GridTrading`、`DCA` 等仍未完成的策略类型。
4. **Agent 层增强**：完善 `TradingAgent` 的策略上下文、提示词和人工确认边界。
5. **UI 层完善**：持续增强策略配置页、监控页和交易历史页的联动能力。
