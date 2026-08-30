# MarketAssistant.DataProviders — AGENTS.md

数据提供者层，封装所有加密货币与 A 股外部 API 调用。当前仅依赖 `MarketAssistant.Core`；不直接依赖 `MarketAssistant.Agents`。

---

## 目录结构

```
MarketAssistant.DataProviders/
├── BinanceMarketDataService.cs    ← Binance REST API（现货 + 合约）
├── BinanceMarketDataModels.cs     ← Binance 本地模型（24hrTicker、ExchangeInfo）
├── BinanceWebSocketService.cs     ← Binance WebSocket 实时价格推送
├── CoinGeckoApiService.cs         ← CoinGecko REST API（市值、排名、涨跌幅）
├── AShare/                        ← A 股数据客户端（P1-05 下沉）
│   ├── ClsQuoteClient.cs          ← 财联社行情 /quote/stock/basic 与搜索 /api/sw
│   ├── ZhiTuMarketClient.cs       ← 智兔财务/技术指标/K线/资金流/公司资料
│   ├── EastMoneyNewsClient.cs     ← 东方财富搜索新闻（JSONP）
│   ├── ClsStockQuoteData.cs       ← CLS 行情字段模型
│   └── ServiceCollectionExtensions.cs ← AddAShareDataProviders()
├── StringToDecimalConverter.cs    ← JSON 字符串 → decimal 转换器
└── GlobalUsing.cs
```

---

## 外部 API 说明

| 服务 | 域名 | 注意事项 |
|------|------|---------|
| **Binance** | `api.binance.com` / `fapi.binance.com` | 部分地区受限，需 VPN/代理 |
| **CoinGecko** | `api.coingecko.com` | 免费版有频率限制（~24 req/min），`CoinGeckoApiService` 内置限流 |
| **财联社 (Cls)** | `x-quote.cls.cn` / `www.cls.cn` | 命名 HttpClient `Cls`；行情无需签名 |
| **智兔 (ZhiTu)** | `api.zhituapi.com` | 命名 HttpClient `ZhiTu`；Token 由调用方传入，禁止硬编码 |
| **东方财富** | `search-api-web.eastmoney.com` | 命名 HttpClient `EastMoneySearch`；返回 JSONP 需剥离 |

---

## 编码约定

- 所有外部 HTTP 调用通过命名 HttpClient（如 `CreateClient("Binance")`）自动获得弹性策略（重试、超时、熔断），无需手动包裹。
- 网络/API 错误应包装为 `FriendlyException` 抛出，消息面向用户可读。
- API 响应的反序列化模型：
  - 本地模型（如 `Binance24hrTicker`）定义在本项目。
  - 若某个响应模型仅服务于数据提供者内部调用，继续保留在本项目；若它已成为跨模块工具契约的一部分，再评估是否上移到契约层。
  - A 股客户端的泛型方法（`GetListAsync<T>` 等）允许上层 DTO 类型作为 T，HTTP 与容错反序列化统一在本层完成；Tool/业务层禁止再直接 `GetStringAsync` + 手解析。
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
