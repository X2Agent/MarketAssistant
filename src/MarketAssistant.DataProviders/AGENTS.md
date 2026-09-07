# MarketAssistant.DataProviders — AGENTS.md

数据提供者层，封装加密货币、A 股与 Web3 链上外部 API 调用。当前仅依赖 `MarketAssistant.Core`；不直接依赖 `MarketAssistant.Agents`。

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
├── Web3/                           ← 链上数据客户端（DexScreener + GoPlus）
│   ├── DexScreenerClient.cs        ← 多链 DEX 行情：热门对搜索 /token-pairs（免费无 Key）
│   ├── GoPlusSecurityClient.cs     ← 代币安全审计 / 蜜罐检测 / 钱包安全 / 大额授权
│   ├── ChainRegistry.cs            ← 链 ID 统一归一化（规范名 / 数字链 ID / CoinGecko 平台 ID / 别名互转）
│   ├── Web3Models.cs               ← DexScreener / GoPlus 响应模型（本层内部使用）
│   └── ServiceCollectionExtensions.cs ← AddWeb3DataProviders()
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
| **DexScreener** | `api.dexscreener.com` | 命名 HttpClient `DexScreener`；免费公开接口无需 Key，限速 300 req/min；`token-pairs` 端点要求链全名（ethereum/bsc...），错误写法会**静默返回空数组** |
| **GoPlus Security** | `api.gopluslabs.io` | 命名 HttpClient `GoPlus`；免费接口有 QPS 限制，API Key 可选（经 `GoPlusSecurityClient.ApiKey` 属性由上层注入，本层不感知设置服务）；主端点要求**数字链 ID**（1/56/137...），Solana 走独立端点 `/api/v1/solana/token_security`（旧域名 api.gopluslabs.com 已退役，禁止使用） |

### 链 ID 词汇约定（重要）

各数据源对同一条链的命名不一致，且错误输入大多不报错而是返回空数据：

| 规范链名 | CoinGecko 平台 ID | DexScreener | GoPlus |
|---------|------------------|-------------|--------|
| ethereum | ethereum | ethereum | 1 |
| bsc | binance-smart-chain | bsc | 56 |
| polygon | polygon-pos | polygon | 137 |
| arbitrum | arbitrum-one | arbitrum | 42161 |
| optimism | optimistic-ethereum | optimism | 10 |
| avalanche | avalanche | avalanche | 43114 |
| base | base | base | 8453 |
| solana | solana | solana | 专用端点 |

- 所有 Web3 客户端的链 ID 入参**必须经 `ChainRegistry.Normalize()` 归一化**后再拼 URL，禁止直接透传用户/LLM 原始输入。
- `Normalize()` 兼容规范名、`eth/1`、`binance-smart-chain`、`polygon-pos`、`arbitrum-one` 等别名；未收录链小写透传（支持 DexScreener 长尾链），GoPlus 侧由 `RequireGoPlusEvmChain` 明确报错。
- 新增链支持时：在 `ChainRegistry.Chains`/`Aliases` 各加一行即可，客户端代码无需改动。

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
