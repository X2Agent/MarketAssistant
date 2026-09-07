# MarketAssistant.Trading — AGENTS.md

共享交易契约项目，定义交易所统一接口、交易模型与跨层共享 DTO。活跃的交易编排与执行实现位于 `MarketAssistant.App.Services`；当前目录负责稳定契约边界，而非运行时编排。

---

## 目录结构

```
MarketAssistant.Trading/
├── TradingEnums.cs           ← 跨层共享枚举（StrategyType / OrderSide / CryptoTradingMode 等）
├── TradingStrategy.cs        ← 交易策略配置（持久化模型）
├── TradeRecord.cs            ← 交易执行记录（持久化模型）
├── RiskModels.cs             ← RiskConfig / RiskCheckResult / DailyStats
├── StrategyParams.cs         ← GridTradingParams / DCAParams 策略参数
├── TradingViewModels.cs      ← Agent 视图 DTO + Position + TradingContext
├── Abstractions/
│   └── IExchangeClient.cs   ← 交易所客户端契约 + 相关 DTO
└── MarketAssistant.Trading.csproj
```

---

## 关键类型

| 类型 | 用途 |
|------|------|
| `IExchangeClient` | 统一交易所客户端接口：获取账户信息、下单、查单、撤单、获取持仓 |
| `ExchangeAccountInfo` | 账户信息：`CanTrade`、`Balances` |
| `ExchangeBalance` | 资产余额：`Asset`、`Free`、`Locked` |
| `ExchangeOrderResult` | 订单结果：`OrderId`、`Status`、`ExecutedQty`、`Price` |

---

## 当前定位

- 本项目是主线引用中的共享契约层，由 `App`、`App.Services`、`Agents` 等项目共同引用。
- 这里只放稳定接口、跨层共享模型、轻量上下文对象；不要放交易流程编排、风控实现、持久化、Agent 决策或 UI 逻辑。
- 新增交易运行时能力，放在 `MarketAssistant.App.Services/Trading/` 或 `MarketAssistant.App.Services/Agents/Tools/Crypto/`。
- 若后续需要进一步收敛边界，优先把契约继续稳定在此层，而不是把实现回填进来。

---

## 构建

```bash
dotnet build src/MarketAssistant.Trading/MarketAssistant.Trading.csproj -c Debug
```
