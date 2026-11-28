# MarketAssistant.DataProviders — AGENTS.md

数据提供者层，封装加密货币、A 股与 Web3 链上外部 API 调用。当前仅依赖 `MarketAssistant.Core`；不直接依赖 `MarketAssistant.Agents`。

---

## 目录结构

```
MarketAssistant.DataProviders/
├── BinanceMarketDataService.cs    ← Binance REST API（现货公共行情走只读主机 data-api.binance.vision，合约走 fapi）
├── BinanceMarketDataModels.cs     ← Binance 本地模型（24hrTicker、ExchangeInfo）
├── BinanceWebSocketService.cs     ← Binance WebSocket 实时价格推送（只读行情流 data-stream.binance.vision）
├── BinanceFundingRateResponse.cs  ← 资金费率响应模型
├── BinanceLongShortRatioResponse.cs ← 多空比响应模型
├── BinanceOpenInterestResponse.cs ← 持仓量响应模型
├── BinancePremiumIndexResponse.cs ← 溢价指数响应模型
├── BinanceRegionUnavailableException.cs ← 地区限制异常（binance.com 返回451 等）
├── CoinGeckoApiService.cs         ← CoinGecko REST API（市值、排名、涨跌幅）
├── CoinGeckoSearchResponse.cs     ← CoinGecko 搜索响应模型
├── CoinGeckoTickersResponse.cs    ← CoinGecko 行情响应模型
├── AlternativeMeClient.cs        ← 恐惧贪婪指数（alternative.me）
├── AShare/                        ← A 股数据客户端（P1-05 下沉）
│   ├── ClsQuoteClient.cs          ← 财联社行情 /quote/stock/basic 与搜索 /api/sw
│   ├── ZhiTuMarketClient.cs       ← 智兔财务/技术指标/K线/资金流/公司资料
│   ├── EastMoneyNewsClient.cs     ← 东方财富搜索新闻（JSONP）
│   ├── SinaIndexQuoteClient.cs    ← 新浪指数行情（顶栏行情条）
│   ├── SinaStockQuoteClient.cs    ← 新浪个股行情（五档盘口 + 开高低收快照字段；不含逐笔）
│   ├── SinaKLineClient.cs         ← 新浪 K 线历史（智兔不覆盖基金/ETF 时的兜底数据源）
│   ├── SinaFundFlowClient.cs      ← 新浪个股资金流排行
│   ├── SinaFundFlowItem.cs        ← 新浪资金流条目模型
│   ├── ClsStockQuoteData.cs       ← CLS 行情字段模型
│   ├── AShareJsonOptions.cs       ← A 股容错反序列化选项（字符串数值/null/-- 占位）
│   └── ServiceCollectionExtensions.cs ← AddAShareDataProviders()
├── Web3/                           ← 链上数据客户端（DexScreener + GoPlus）
│   ├── DexScreenerClient.cs        ← 多链 DEX 行情：热门对搜索 /token-pairs（免费无 Key）
│   ├── GoPlusSecurityClient.cs     ← 代币安全审计 / 蜜罐检测（EVM 主端点 + Solana 专用端点）
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
| **Binance** | `data-api.binance.vision` / `fapi.binance.com` / `api.binance.com` | 三个命名 HttpClient 各管一类：`BinanceMarketData`（现货公共行情 REST → `data-api.binance.vision`，Binance 官方的只读行情主机，响应格式与 `api.binance.com` 一致，实测在后者返回 451 的出口上仍可访问）、`BinanceFutures`（合约 → `fapi.binance.com`，部分地区受限）、`Binance`（签名交易 / 账户 / 时间同步 → `api.binance.com`，部分地区受限，需代理）。**现货公共端点必须经 `BinanceMarketDataService.SpotClientName` 取 `BinanceMarketData` 客户端**，不要回退到 `Binance`：后者在受限出口返回 451，会让行情、盘口、K 线与选币求交整体不可用；现货行情 WebSocket 同理走 `data-stream.binance.vision`。回归测试：`BinanceMarketDataServiceClientRoutingTest` |
| **CoinGecko** | `api.coingecko.com` | 免费版有频率限制（~24 req/min），`CoinGeckoApiService` 内置限流 |
| **财联社 (Cls)** | `x-quote.cls.cn` / `www.cls.cn` | 命名 HttpClient `Cls`；行情无需签名 |
| **智兔 (ZhiTu)** | `api.zhituapi.com` | 命名 HttpClient `ZhiTu`；Token 由调用方传入，禁止硬编码；`/hs/history` K 线不覆盖基金/ETF（返回 404），由 `SinaKLineClient` 兜底；资金流 `/hs/history/transaction` 的 `t` 是字符串 `yyyy-MM-dd HH:mm:ss`（非数字），且不带 `st`+`et`（`yyyyMMdd`，须成对、≤365 天，单侧返回 422）只返回最近 1 个交易日，`lt` 对其无效 |
| **新浪行情 (Sina)** | `hq.sinajs.cn` / `quotes.sina.cn` | 命名 HttpClient `SinaHq`（个股五档，强制 Referer、GBK 响应）与 `SinaKLine`（K 线历史，实测无需 Referer、无限流）；`quotes.sina.cn` datalen 上限 1900、传 0 返回全历史，无效代码/周期返回 JSON `null` |
| **东方财富** | `search-api-web.eastmoney.com` | 命名 HttpClient `EastMoneySearch`；返回 JSONP 需剥离 |
| **DexScreener** | `api.dexscreener.com` | 命名 HttpClient `DexScreener`；免费公开接口无需 Key，限速 300 req/min；`token-pairs` 端点要求链全名（ethereum/bsc...），错误写法会**静默返回空数组** |
| **GoPlus Security** | `api.gopluslabs.io` | 命名 HttpClient `GoPlus`；免费接口限流很严（连续数次请求即返回 `code=4029 too many requests`，联调时请间隔调用），API Key 可选（经 `GoPlusSecurityClient.ApiKey` 属性由上层注入，本层不感知设置服务）；EVM 主端点 `/api/v1/token_security/{数字链 ID}`（1/56/137...）与 Solana 专用端点 `/api/v1/solana/token_security` 响应结构不同，分别建模为 `GoPlusTokenSecurity` / `GoPlusSolanaTokenSecurity`（旧域名 api.gopluslabs.com 已退役，禁止使用）。**字段名与类型以官方 Response Details 为准**，写错不会报错而是静默失效：EVM 标志位是 `"1"`/`"0"` 字符串，缺失表示未知（曾因写成 `is_black_list` / `self_destruct` / `honeypot_with_same_owner` 而对应风险检查形同虚设，正名为 `is_blacklisted` / `selfdestruct` / `honeypot_with_same_creator`；持仓占比的正名是 `owner_percent` / `creator_percent`，不是 `*_percentage`。DTO 只声明被读取的字段，唯一例外是有意保留的 `token_name` / `owner_percent` / `creator_percent`（代币名、所有者与创建者持仓占比，尚无读取方，留给报告展示持仓集中度），新增其他字段前先确认有调用方），`trust_list` 是字符串，`is_in_cex` 与 `fake_token` 是对象，`owner_address` 为空串才表示无所有者；Solana 的 `trusted_token` 是数字、各 `status` 与标志位是字符串，统一用 `int?` 承接（依赖 `AllowReadingFromString`），权限类字段是 `{authority:[{address,malicious_address}],status}` 对象。风险判定与可信代币豁免在 App.Services 的 `CryptoOnChainTools`，见其 AGENTS.md |

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
- `Normalize()` 兼容规范名、`eth/1`、`binance-smart-chain`、`polygon-pos`、`arbitrum-one` 等别名；未收录链小写透传（支持 DexScreener 长尾链），GoPlus 侧由 `ResolveGoPlusChainId` 明确报错。
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
