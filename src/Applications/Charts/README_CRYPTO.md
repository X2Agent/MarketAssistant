# 虚拟币 K 线数据服务说明

## 概述

`CryptoKLineService` 基于[币安 REST API](https://developers.binance.com/docs/zh-CN/binance-spot-api-docs/rest-api) 实现，用于获取虚拟货币（加密货币）的 K 线数据。

## 技术架构

### API 提供商
- **币安（Binance）**: 全球领先的加密货币交易所
- **API 文档**: https://developers.binance.com/docs/zh-CN/binance-spot-api-docs/rest-api
- **Base URL**: `https://api.binance.com`

### 认证方式
- **公开市场数据**: 无需 API Key，直接访问
- **限流规则**: 
  - 单个 IP 每分钟最多 1200 次请求
  - 权重限制：每分钟 6000 权重
  - K 线接口权重：2

## 支持的时间周期

根据币安 API 的 `interval` 参数，支持以下周期：

| 方法 | 币安 Interval | 说明 |
|------|---------------|------|
| `GetMinuteKLineDataAsync` | `1m` | 1分钟K线 |
| `Get5MinuteKLineDataAsync` | `5m` | 5分钟K线 |
| `Get15MinuteKLineDataAsync` | `15m` | 15分钟K线 |
| `Get30MinuteKLineDataAsync` | `30m` | 30分钟K线 |
| `Get60MinuteKLineDataAsync` | `1h` | 1小时K线 |
| `GetDailyKLineDataAsync` | `1d` | 日K线 |
| `GetWeeklyKLineDataAsync` | `1w` | 周K线 |
| `GetMonthlyKLineDataAsync` | `1M` | 月K线 |

## 交易对格式

### 输入格式（灵活）
服务会自动处理以下格式：

- `BTCUSDT` ✅ 推荐
- `BTC` ✅ 自动添加 USDT 后缀
- `btcusdt` ✅ 自动转大写
- `crypto.BTCUSDT` ✅ 自动移除前缀

### 支持的交易对
常见交易对包括：
- **USDT 交易对**: BTCUSDT, ETHUSDT, BNBUSDT, SOLUSDT 等
- **BTC 交易对**: ETHBTC, BNBBTC 等
- **ETH 交易对**: BNBETH 等

完整列表参考: https://api.binance.com/api/v3/exchangeInfo

## K 线数据格式

### 币安原始响应（JSON 数组）

```json
[
  [
    1499040000000,      // [0] 开盘时间（毫秒）
    "0.01634000",       // [1] 开盘价
    "0.80000000",       // [2] 最高价
    "0.01575800",       // [3] 最低价
    "0.01577100",       // [4] 收盘价
    "148976.11427815",  // [5] 成交量
    1499644799999,      // [6] 收盘时间（毫秒）
    "2434.19055334",    // [7] 成交额
    308,                // [8] 成交笔数
    "1756.87402397",    // [9] 主动买入成交量
    "28.46694368",      // [10] 主动买入成交额
    "0"                 // [11] 忽略
  ]
]
```

### 转换后的应用模型（KLineData）

```csharp
public class KLineData
{
    public DateTime Timestamp { get; set; }    // 时间戳
    public decimal Open { get; set; }          // 开盘价
    public decimal High { get; set; }          // 最高价
    public decimal Low { get; set; }           // 最低价
    public decimal Close { get; set; }         // 收盘价
    public decimal Volume { get; set; }        // 成交量
    public decimal Amount { get; set; }        // 成交额
    public decimal PreClose { get; set; }      // 昨收价（计算得出）
    public decimal Change { get; set; }        // 涨跌额（计算得出）
    public decimal PctChg { get; set; }        // 涨跌幅%（计算得出）
}
```

## 使用示例

### 基本用法

```csharp
// 通过依赖注入获取服务
var klineService = serviceProvider.GetRequiredKeyedService<IKLineService>(MarketType.Crypto);

// 获取比特币日K线（最近500条）
var btcDailyData = await klineService.GetDailyKLineDataAsync("BTCUSDT", 500);

// 获取以太坊15分钟K线
var ethData = await klineService.Get15MinuteKLineDataAsync("ETH", 100);

// 获取币安币周K线
var bnbData = await klineService.GetWeeklyKLineDataAsync("BNBUSDT", 52);
```

### 错误处理

```csharp
try
{
    var data = await klineService.GetDailyKLineDataAsync("BTCUSDT");
}
catch (FriendlyException ex)
{
    // 用户友好的错误信息
    Console.WriteLine($"获取K线数据失败: {ex.Message}");
}
catch (HttpRequestException ex)
{
    // 网络连接错误
    Console.WriteLine($"网络错误: {ex.Message}");
}
```

## 限制与注意事项

### 数据量限制
- **单次请求最大**: 1000 条（币安API限制）
- **默认数量**: 500 条
- **建议数量**: 
  - 分钟级: 100-500 条
  - 小时级: 500-1000 条
  - 日K/周K/月K: 根据需要调整

### 实时性
- **数据延迟**: < 1 秒（币安实时数据）
- **无需缓存**: 数据有实时性要求，不推荐缓存
- **更新频率**: 根据时间周期实时更新

### 网络要求
- **连接超时**: 30 秒
- **重试机制**: 未实现，需要调用方处理
- **代理支持**: 可通过 HttpClient 配置

### 合规性
- **公开数据**: 无需 API Key，可直接访问
- **使用条款**: 遵守币安 API 使用条款
- **费率限制**: 避免频繁请求，建议间隔 > 1 秒

## 与 A 股服务的对比

| 特性 | CryptoKLineService (币安) | AShareKLineService (智图) |
|------|--------------------------|--------------------------|
| 数据源 | 币安 API | 智图 API |
| 认证 | 无需 API Key | 需要 Token |
| 市场 | 全球虚拟币 | A股市场 |
| 交易时间 | 24/7 全天候 | 交易日 9:30-15:00 |
| 数据延迟 | < 1秒 | < 5秒 |
| 限流 | 1200次/分钟 | 依智图API |
| 数据格式 | JSON 数组 | JSON 对象 |

## 故障排查

### 问题：返回空数据
- **原因**: 交易对不存在或格式错误
- **解决**: 检查交易对名称，确保在币安上市

### 问题：HTTP 429 错误
- **原因**: 请求频率过高，触发限流
- **解决**: 降低请求频率，添加延迟

### 问题：超时错误
- **原因**: 网络连接问题或币安服务器响应慢
- **解决**: 检查网络连接，考虑使用代理

## 参考链接

- [币安 API 文档（中文）](https://developers.binance.com/docs/zh-CN/binance-spot-api-docs/rest-api)
- [K 线接口说明](https://developers.binance.com/docs/zh-CN/binance-spot-api-docs/rest-api/market-data-endpoints)
- [枚举定义](https://developers.binance.com/docs/zh-CN/binance-spot-api-docs/enums)
- [错误代码](https://developers.binance.com/docs/zh-CN/binance-spot-api-docs/error-codes)
- [币安交易对列表](https://api.binance.com/api/v3/exchangeInfo)

## 更新日志

### v1.0.0 (2025-01-04)
- ✅ 实现币安 K 线数据获取
- ✅ 支持 8 种时间周期（1m, 5m, 15m, 30m, 1h, 1d, 1w, 1M）
- ✅ 自动格式化交易对代码
- ✅ 计算涨跌额和涨跌幅
- ✅ 友好的错误处理
- ✅ 完整的日志记录

