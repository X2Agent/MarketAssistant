# MarketAssistant.Trading — AGENTS.md

交易抽象层，定义交易所客户端统一接口。当前极其精简——仅一个接口和几个 DTO。依赖 `MarketAssistant.Core`（使用 `OrderSide`、`OrderType` 等交易枚举）。

---

## 目录结构

```
MarketAssistant.Trading/
└── Abstractions/
    └── IExchangeClient.cs   ← 交易所客户端接口 + 相关 DTO
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

## 扩展约定

- 新增交易所支持时，在 `MarketAssistant.App/Trading/Exchanges/` 创建实现类（如 `BinanceExchangeClient`）。
- 实现类通过 DI 注册，不在本项目中引入具体 API 依赖。
- 交易相关的枚举和模型（`StrategyType`、`TradeRecord`、`RiskConfig` 等）定义在 `MarketAssistant.Core/Trading/Models/`，而非本项目。
- 本项目应保持最小化——仅放接口和必要的 DTO。

---

## 构建

```bash
dotnet build src/MarketAssistant.Trading/MarketAssistant.Trading.csproj -c Debug
```
