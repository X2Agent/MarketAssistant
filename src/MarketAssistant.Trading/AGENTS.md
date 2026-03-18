# MarketAssistant.Trading — AGENTS.md

遗留交易抽象项目，定义交易所客户端统一接口。**当前目录仍存在，但已不在主解决方案引用链中**；活跃的交易编排与执行实现位于 `MarketAssistant.App.Services`。

---

## 目录结构

```
MarketAssistant.Trading/
├── TradingModels.cs          ← 遗留交易模型
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

## 当前状态

- 本项目不是当前主线开发入口；不要继续在这里新增交易实现。
- 新增交易 Agent、策略工具、执行工具、风控与持久化实现，放在 `MarketAssistant.App.Services/Trading/` 或 `MarketAssistant.App.Services/Agents/Tools/Crypto/`。
- 若未来决定恢复独立交易抽象层，应先同步更新根目录 `AGENTS.md`、解决方案文件和项目引用，再恢复在本项目扩展。
- 在未恢复主线引用前，这里的文件仅视为遗留代码，不作为架构边界依据。

---

## 构建

```bash
dotnet build src/MarketAssistant.Trading/MarketAssistant.Trading.csproj -c Debug
```
