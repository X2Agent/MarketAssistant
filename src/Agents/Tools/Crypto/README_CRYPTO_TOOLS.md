# 虚拟币市场 Tools 实现说明

本文档说明虚拟币市场在 `Tools/Abstractions` 下各抽象接口的实现情况和所需 API。

---

## 📊 实现状态总览

| 抽象接口 | 实现状态 | 使用的 API | 实现文件 |
|---------|---------|-----------|----------|
| **IBasicDataTools** | ✅ 部分实现 | 币安 API + 需补充 CoinGecko | `CryptoBasicTools.cs` |
| **ITechnicalDataTools** | ✅ 完全实现 | 币安 K线数据（本地计算） | `CryptoTechnicalTools.cs` |
| **INewsDataTools** | ⚠️ 未实现（已说明） | 需 CryptoCompare API | `CryptoNewsTools.cs` |
| **ISentimentDataTools** | ⚠️ 未实现（已说明） | 币安 Futures API + 第三方 | `CryptoSentimentTools.cs` |
| **IFinancialDataTools** | ❌ 不适用（已说明） | 虚拟币无财务报表概念 | `CryptoFinancialTools.cs` |

---

## 1️⃣ IBasicDataTools - 基础数据工具

### ✅ 已实现：GetAssetInfoAsync

**使用 API**: 币安 REST API

```
GET https://api.binance.com/api/v3/ticker/24hr?symbol={symbol}
```

**提供数据**:
- ✅ 当前价格（lastPrice）
- ✅ 24h 涨跌幅（priceChangePercent）
- ✅ 24h 最高/最低价（highPrice, lowPrice）
- ✅ 24h 交易量（volume, quoteVolume）
- ✅ 开盘价（openPrice）
- ✅ 昨收价（prevClosePrice）
- ✅ 加权平均价（weightedAvgPrice）

**实现特点**:
- 自动格式化交易对符号（如 "BTC" → "BTCUSDT"）
- 计算振幅（Amplitude）
- 映射到 `AssetQuoteInfo` 模型
- 虚拟币不支持的字段（如市盈率、换手率）设为 0

**使用示例**:
```csharp
var basicTools = serviceProvider.GetRequiredKeyedService<IBasicDataTools>(MarketType.Crypto);
var quoteInfo = await basicTools.GetAssetInfoAsync("BTC");
Console.WriteLine($"BTC 当前价: ${quoteInfo.CurrentPrice}, 涨跌幅: {quoteInfo.PercentageChange}%");
```

---

### ⚠️ 未实现：GetCompanyInfoAsync

**需要 API**: CoinGecko API（免费，无需 API Key）

```
GET https://api.coingecko.com/api/v3/coins/{id}
示例: https://api.coingecko.com/api/v3/coins/bitcoin
```

**可提供数据**:
- 项目描述（description.en）
- 官方网站（links.homepage）
- 区块链浏览器（links.blockchain_site）
- 源代码仓库（links.repos_url.github）
- 所属类别（categories: ["Layer 1", "DeFi", "NFT"]）
- 社区数据（twitter_followers, reddit_subscribers）
- 开发者数据（developer_data.stars, forks, commits）

**实现步骤**:
1. 建立交易对符号到 CoinGecko ID 的映射表（如 BTC→bitcoin, ETH→ethereum）
2. 调用 `/api/v3/coins/{id}` 获取项目详情
3. 映射到 `CompanyInfo` 模型：
   - `Description`: 项目描述（description.en）
   - `MainBusiness`: 主要应用场景（从 categories 提取）
   - `Industry`: 所属类别（categories[0]）

**替代方案**: CoinMarketCap API（需要 API Key，有免费额度）

---

## 2️⃣ ITechnicalDataTools - 技术分析工具

### ✅ 已全部实现（基于币安 K 线数据本地计算）

**数据源**: 复用已实现的 `CryptoKLineService`

```csharp
// 通过 IKLineService 获取 K 线数据
var klineService = serviceProvider.GetRequiredKeyedService<IKLineService>(MarketType.Crypto);
var klineData = await klineService.GetKLineDataAsync("BTC", KLineType.Daily, 100);
```

#### ✅ GetKDJAsync - KDJ 随机指标

**计算方法**:
1. 计算 RSV (Raw Stochastic Value)
2. 计算 K 值: `K = 2/3 * 前一日K + 1/3 * RSV`
3. 计算 D 值: `D = 2/3 * 前一日D + 1/3 * K`
4. 计算 J 值: `J = 3K - 2D`

**数据要求**: 至少 9 日 K 线数据

#### ✅ GetMACDAsync - MACD 指标

**计算方法**:
1. 计算 EMA12 和 EMA26
2. 计算 DIFF: `EMA12 - EMA26`
3. 计算 DEA: DIFF 的 9 日 EMA
4. 计算 MACD 柱: `(DIFF - DEA) * 2`

**数据要求**: 至少 26 日 K 线数据（推荐 50 日）

#### ✅ GetBOLLAsync - 布林带指标

**计算方法**:
1. 计算中轨: 20 日 SMA
2. 计算标准差
3. 计算上轨: `中轨 + 2 * 标准差`
4. 计算下轨: `中轨 - 2 * 标准差`

**数据要求**: 至少 20 日 K 线数据

#### ✅ GetMAAsync - 移动平均线

**支持周期**: MA3, MA5, MA10, MA15, MA20, MA30, MA60, MA120, MA200, MA250

**计算方法**: 简单移动平均线（SMA）

**数据要求**: 最多 250 日 K 线数据（根据需要的最大周期）

**使用示例**:
```csharp
var technicalTools = serviceProvider.GetRequiredKeyedService<ITechnicalDataTools>(MarketType.Crypto);

// 获取 KDJ 指标
var kdj = await technicalTools.GetKDJAsync("BTC");
Console.WriteLine($"KDJ: K={kdj.K}, D={kdj.D}, J={kdj.J}");

// 获取 MACD 指标
var macd = await technicalTools.GetMACDAsync("ETH");
Console.WriteLine($"MACD: DIFF={macd.Diff}, DEA={macd.Dea}, MACD={macd.Macd}");

// 获取布林带
var boll = await technicalTools.GetBOLLAsync("BTC");
Console.WriteLine($"BOLL: 上轨={boll.U}, 中轨={boll.M}, 下轨={boll.D}");

// 获取均线
var ma = await technicalTools.GetMAAsync("BTC");
Console.WriteLine($"MA: MA5={ma.MA5}, MA10={ma.MA10}, MA20={ma.MA20}");
```

---

## 3️⃣ INewsDataTools - 新闻数据工具

### ⚠️ 未实现（已提供详细说明）

**推荐 API**: CryptoCompare News API（免费，项目已集成）

```
GET https://min-api.cryptocompare.com/data/v2/news/?categories={symbol}&lang=EN&sortOrder=latest
示例: https://min-api.cryptocompare.com/data/v2/news/?categories=BTC&lang=EN
```

**提供数据**:
- ✓ 新闻标题（title）
- ✓ 新闻正文（body）
- ✓ 来源网站（source）
- ✓ 发布时间（published_on，Unix 时间戳）
- ✓ 新闻链接（url）
- ✓ 图片链接（imageurl）
- ✓ 分类标签（categories）

**实现步骤**:
1. 从 `IUserSettingService` 获取 `CryptoCompareApiKey`（可选，无 Key 也可用但有限额）
2. 构建请求 URL: `GET /data/v2/news/?categories={symbol}&lang=EN&sortOrder=latest`
3. 解析 JSON 响应中的 Data 数组
4. 映射到 `NewsItem` 模型：
   - `Title`: 新闻标题（title）
   - `Content`: 新闻正文（body，取前 300 字）
   - `PublishTime`: 发布时间（从 published_on Unix 时间戳转换）
   - `Source`: 来源（source）
   - `Url`: 原文链接（url）
5. 按时间倒序排序，返回最新的 count 条

**性能建议**: 添加缓存机制（如 `IMemoryCache`），避免频繁请求

**替代方案**:
- CryptoPanic API（需注册免费 API Token）
- Playwright 爬取 Twitter/X（无需 API，但实现复杂度高）

---

## 4️⃣ ISentimentDataTools - 市场情绪工具

### ⚠️ 未实现（已提供详细说明）

**可分阶段实现**:

### 阶段 1: 币安 Futures API（可立即实现，优先级 P0）

#### 1. 资金费率（Funding Rate）

```
GET https://fapi.binance.com/fapi/v1/fundingRate?symbol={symbol}
```

**说明**: 反映多空情绪
- 正值：多头支付空头（市场看多）
- 负值：空头支付多头（市场看空）

#### 2. 多空持仓人数比

```
GET https://fapi.binance.com/futures/data/globalLongShortAccountRatio?symbol={symbol}&period=5m
```

**参数**: period 可选 5m/15m/30m/1h/2h/4h/6h/12h/1d

**返回**: `longAccount`（多头人数比）, `shortAccount`（空头人数比）

#### 3. 大户多空持仓比

```
GET https://fapi.binance.com/futures/data/topLongShortAccountRatio?symbol={symbol}&period=5m
```

**说明**: Top Trader（大户）的持仓情况，更有参考价值

#### 4. 合约持仓量（Open Interest）

```
GET https://fapi.binance.com/fapi/v1/openInterest?symbol={symbol}
```

**说明**: 未平仓合约总量，反映市场活跃度

### 阶段 2: 恐慌贪婪指数（优先级 P1）

**API**: Alternative.me Fear & Greed Index（免费，无需 API Key）

```
GET https://api.alternative.me/fng/
```

**返回数据**:
- `value`: 0-100（0=极度恐慌，100=极度贪婪）
- `value_classification`: 文字描述（Extreme Fear, Fear, Neutral, Greed, Extreme Greed）

### 阶段 3: Twitter 情绪分析（优先级 P2）

**方案 A**: 爬取 Twitter 推文 + 本地情感分析模型

**方案 B**: 使用第三方情感分析 API（如 Google NLP, Azure Text Analytics）

**方案 C**: 使用 LunarCrush API（提供社交媒体情绪数据）

### 模型适配建议

`FundFlow` 模型原为 A 股设计（主力/超大单/大单/中单/小单流入流出），虚拟币无此概念。

**建议**:
- 扩展模型或创建新模型（如 `CryptoSentiment`）
- 或复用字段映射：
  - `MainNetInflow` → 资金费率
  - `SuperLargeNetInflow` → 大户多头比例
  - `LargeNetInflow` → 多空持仓人数比
  - `MediumNetInflow` → 恐慌贪婪指数
  - `SmallNetInflow` → 合约持仓量变化

---

## 5️⃣ IFinancialDataTools - 财务数据工具

### ❌ 不适用（虚拟币无传统财务报表概念）

虚拟币项目没有传统的资产负债表、利润表、现金流量表等财务报表。

### 可选替代方案

#### 代币供应量数据

**API**: CoinGecko API

```
GET https://api.coingecko.com/api/v3/coins/{id}
```

**提供数据**:
- `total_supply`: 总供应量
- `circulating_supply`: 流通供应量
- `max_supply`: 最大供应量

#### 协议收入数据（仅适用于 DeFi 协议）

**API**: Token Terminal API

**提供数据**:
- Revenue（协议收入）
- Protocol Earnings（协议净收入）
- P/S Ratio（市销率）
- P/E Ratio（市盈率，极少协议有）

#### 链上资金流动分析

**API**: Glassnode API

**可提供数据**:
- Exchange Netflow（交易所净流入/流出）
- Whale Transactions（大额交易）
- Active Addresses（活跃地址数）
- Transaction Volume（交易量）

#### 链上估值指标

**API**: Glassnode, CoinMetrics

**可提供指标**:
- MVRV Ratio（市值/实现市值比）
- NVT Ratio（网络价值/交易量比）
- 活跃地址增长率
- 持币地址集中度
- Staking 比率（适用于 PoS 币种）

#### 代币分配和解锁信息

**API**: Messari API

**可提供数据**:
- Initial Distribution（初始分配）
- Team/Investor Allocation（团队/投资者份额）
- Vesting Schedule（解锁计划）

---

## 📦 依赖项总结

### 必需（已集成）
- ✅ 币安 API（K 线数据、24h 行情）
- ✅ .NET System.Text.Json（JSON 解析）
- ✅ Microsoft.Extensions.AI（AI Function 工厂）
- ✅ Microsoft.Extensions.Logging（日志记录）

### 推荐补充（免费 API）
- ⚠️ CoinGecko API: 项目信息、代币供应量（免费，无需 Key）
- ⚠️ CryptoCompare API: 新闻数据（项目已集成，免费额度）
- ⚠️ Alternative.me API: 恐慌贪婪指数（免费，无需 Key）

### 可选补充（需注册或付费）
- 🔧 币安 Futures API: 资金费率、多空持仓比（需 Futures 账户）
- 🔧 CoinMarketCap API: 项目信息（需 API Key，有免费额度）
- 🔧 Glassnode API: 链上数据（需订阅）
- 🔧 Token Terminal API: DeFi 协议收入（需订阅）
- 🔧 Messari API: 代币分配信息（需订阅）

---

## 🚀 实现优先级建议

### P0（立即可用）
1. ✅ **IBasicDataTools.GetAssetInfoAsync** - 已实现
2. ✅ **ITechnicalDataTools（所有方法）** - 已实现

### P1（推荐实现）
3. ⚠️ **IBasicDataTools.GetCompanyInfoAsync** - 需 CoinGecko API
4. ⚠️ **INewsDataTools.GetNewsAsync** - 需 CryptoCompare API

### P2（可选实现）
5. ⚠️ **ISentimentDataTools.GetFundFlowAsync** - 需币安 Futures API + 第三方

### P3（暂不实现）
6. ❌ **IFinancialDataTools（所有方法）** - 虚拟币不适用

---

## 📝 使用注意事项

1. **API 限流**: 
   - 币安 API: 每 IP 每分钟 1200 次请求
   - CryptoCompare: 免费额度每秒 50 次请求
   - 建议添加缓存机制

2. **错误处理**:
   - 网络异常处理
   - API 返回错误处理
   - 数据格式验证

3. **符号映射**:
   - 币安使用交易对格式（如 BTCUSDT）
   - CoinGecko 使用 ID 格式（如 bitcoin）
   - 需要建立映射表

4. **数据精度**:
   - 虚拟币价格精度通常为小数点后 2-8 位
   - 建议使用 `decimal` 类型避免精度损失

5. **时区处理**:
   - 币安 API 返回 UTC 时间戳
   - 需要转换为本地时间显示

---

## 🔗 相关文档

- [币安 API 文档](https://developers.binance.com/docs/zh-CN/binance-spot-api-docs)
- [CoinGecko API 文档](https://www.coingecko.com/zh/api/documentation)
- [CryptoCompare API 文档](https://min-api.cryptocompare.com/)
- [Alternative.me Fear & Greed Index](https://alternative.me/crypto/fear-and-greed-index/)

---

**编译状态**: ✅ 所有代码已通过编译验证（0 错误，仅项目既有警告）

**最后更新**: 2025-01-04

