# MarketAssistant.DataProviders — AGENTS.md

数据提供者层，封装所有加密货币外部 API 调用。依赖 `MarketAssistant.Core`（HTTP 重试、异常）和 `MarketAssistant.Agents`（API 响应模型）。

---

## 目录结构

```
MarketAssistant.DataProviders/
├── BinanceMarketDataService.cs    ← Binance REST API（现货 + 合约）
├── BinanceMarketDataModels.cs     ← Binance 本地模型（24hrTicker、ExchangeInfo）
├── BinanceWebSocketService.cs     ← Binance WebSocket 实时价格推送
├── CoinGeckoApiService.cs         ← CoinGecko REST API（市值、排名、涨跌幅）
├── CoinDeskApiService.cs          ← CoinDesk REST API（项目元数据、新闻）
├── StringToDecimalConverter.cs    ← JSON 字符串 → decimal 转换器
└── GlobalUsing.cs
```

---

## 外部 API 说明

| 服务 | 域名 | 注意事项 |
|------|------|---------|
| **Binance** | `api.binance.com` / `fapi.binance.com` | 部分地区受限，需 VPN/代理 |
| **CoinGecko** | `api.coingecko.com` | 免费版有频率限制（~24 req/min），`CoinGeckoApiService` 内置限流 |
| **CoinDesk** | `data-api.coindesk.com` | 无特殊限制 |

---

## 编码约定

- 所有外部 HTTP 调用通过命名 HttpClient（如 `CreateClient("Binance")`）自动获得弹性策略（重试、超时、熔断），无需手动包裹。
- 网络/API 错误应包装为 `FriendlyException` 抛出，消息面向用户可读。
- API 响应的反序列化模型：
  - 本地模型（如 `Binance24hrTicker`）定义在本项目。
  - 与工具接口耦合的模型（如 `BinanceFundingRateResponse`）定义在 `MarketAssistant.Agents/Tools/Models/`。
- 不要在代码中硬编码 API 密钥。密钥通过 App 项目的设置服务注入。
- WebSocket 服务需处理断线重连。
- Binance API 在部分地区需配置 HTTP 代理，代理设置通过 `IUserSettingService` 管理，由 `HttpClient` 构造时注入。

---

## 测试

- 外部 API 调用应通过 mock `HttpMessageHandler` 进行单元测试，避免依赖真实网络。
- 测试位于 `tests/TestMarketAssistant.csproj`。

---

## 构建

```bash
dotnet build src/MarketAssistant.DataProviders/MarketAssistant.DataProviders.csproj -c Debug
```
