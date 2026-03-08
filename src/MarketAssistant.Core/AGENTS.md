# MarketAssistant.Core — AGENTS.md

共享基础层，提供全局枚举、工具类、异常处理和交易模型，被所有其他项目引用。**零项目依赖**——仅依赖 `Microsoft.Extensions.Logging.Abstractions` 和 `Microsoft.Data.Sqlite`。

---

## 目录结构

```
MarketAssistant.Core/
├── Infrastructure/
│   ├── Core/
│   │   ├── MarketType.cs              ← 市场类型枚举（AShare / Crypto）
│   │   ├── FriendlyException.cs       ← 用户友好异常（消息直接展示在 UI）
│   │   ├── ErrorMessageMapper.cs      ← 技术异常 → 用户友好消息映射
│   │   ├── StockSymbolConverter.cs    ← A 股代码格式转换
│   │   ├── CryptoSymbolConverter.cs   ← 加密货币符号转换
│   │   ├── NavigationMessage.cs       ← 页面导航消息
│   │   └── AssetFavoritesChanged.cs   ← 收藏变更事件消息
│   ├── Extensions/
│   │   └── EnumExtensions.cs          ← GetDescription() 扩展
│   └── NavigationParameters.cs        ← 导航参数（StockNavigationParameter）
└── Trading/
    └── Models/
        └── TradingModels.cs           ← 交易枚举与模型（Strategy、TradeRecord、RiskConfig 等）
```

---

## 关键类型

| 类型 | 用途 |
|------|------|
| `MarketType` | 多市场 Keyed Services 的 key，贯穿整个架构 |
| `FriendlyException` | 抛出后消息直接展示给用户，不要用于非用户可见的内部错误 |
| `ErrorMessageMapper` | 静态方法 `GetUserFriendlyMessage(Exception)` |
| `StockSymbolConverter` / `CryptoSymbolConverter` | 不同 API 间的代码格式转换 |
| `TradingModels` | `TradingStrategy`、`TradeRecord`、`RiskConfig`、`TradingContext` 等交易模型 |

---

## 编码约定

- 此项目是**共享基础层**，可包含工具类和模型，但不应包含任何 UI 框架、AI 框架或具体 API 调用逻辑。
- 新增类型应放入对应的命名空间目录（`Infrastructure/Core/`、`Trading/Models/` 等）。
- `MarketType` 枚举新增值时，需同步在所有消费项目中注册对应的 Keyed Service 实现。
- 避免引入重量级 NuGet 包——Core 应保持轻量。

---

## 构建

```bash
dotnet build src/MarketAssistant.Core/MarketAssistant.Core.csproj -c Debug
```
