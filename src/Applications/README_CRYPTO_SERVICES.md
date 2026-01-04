# 虚拟币服务实现说明

## 概述

基于[币安 API](https://developers.binance.com/docs/zh-CN/binance-spot-api-docs/rest-api)完整实现了虚拟币市场的所有核心服务，使应用支持加密货币交易对的搜索、行情查看、K线图表、收藏管理等功能。

## 已实现服务列表

### 1. CryptoAssetInfoService ✅
**路径**: `src/Applications/Assets/CryptoAssetInfoService.cs`

**功能**：
- ✅ 搜索虚拟币交易对（基于币安 exchangeInfo API）
- ✅ 获取虚拟币详细信息（基于币安 ticker/24hr API）
- ✅ 获取热门虚拟币（按24小时交易量排序）

**特性**：
- 自动格式化交易对代码（BTC → BTCUSDT）
- 缓存交易对列表（1小时过期）
- 智能价格格式化（根据价格大小选择精度）
- 交易量格式化（K/M/B 单位）

**API 使用**：
```
GET /api/v3/exchangeInfo        # 搜索交易对
GET /api/v3/ticker/24hr         # 获取价格统计
```

### 2. CryptoHomeService ✅
**路径**: `src/Applications/Home/CryptoHomeService.cs`

**功能**：
- ✅ 搜索虚拟币
- ✅ 获取热门虚拟币
- ✅ 获取最近查看记录
- ✅ 添加到最近查看
- ✅ 添加到收藏

**依赖**：
- `IAssetInfoService` (Keyed: Crypto)
- `IAssetHistoryService` (Keyed: Crypto)
- `IFavoriteService` (Keyed: Crypto)

### 3. CryptoChartService ✅
**路径**: `src/Applications/Charts/CryptoChartService.cs`

**功能**：
- ✅ 根据 KLineType 获取对应周期的K线数据
- ✅ 支持 4 种周期：15分钟、日K、周K、月K

**实现方式**：
包装 `CryptoKLineService`，根据 `KLineType` 枚举调用对应方法。

### 4. CryptoKLineService ✅
**路径**: `src/Applications/Charts/CryptoKLineService.cs`

**功能**：
- ✅ 支持 8 种时间周期（1m, 5m, 15m, 30m, 1h, 1d, 1w, 1M）
- ✅ 自动计算涨跌额和涨跌幅
- ✅ 实时数据，无缓存

**API 使用**：
```
GET /api/v3/klines?symbol={SYMBOL}&interval={INTERVAL}&limit={LIMIT}
```

**详细文档**: [README_CRYPTO.md](./Charts/README_CRYPTO.md)

### 5. CryptoFavoriteService ✅
**路径**: `src/Applications/Favorites/CryptoFavoriteService.cs`

**功能**：
- ✅ 添加虚拟币到收藏
- ✅ 从收藏中移除
- ✅ 检查是否已收藏
- ✅ 获取收藏列表
- ✅ 获取收藏虚拟币的最新数据
- ✅ 清空所有收藏

**存储**：
- 使用 `Preferences.Default`（MAUI 本地存储）
- 存储 Key: `FavoriteAssets_Crypto`
- 数据格式: JSON 序列化的 `List<FavoriteAsset>`

### 6. CryptoHistoryService ✅
**路径**: `src/Applications/History/CryptoHistoryService.cs`

**功能**：
- ✅ 添加到历史记录
- ✅ 获取历史记录
- ✅ 清空历史记录
- ✅ 最多保留 10 条记录（FIFO）

**存储**：
- 使用 `Preferences.Default`
- 存储 Key: `RecentAssets_Crypto`
- 数据格式: JSON 序列化的 `List<AssetItem>`

### 7. CryptoTelegramService ✅
**路径**: `src/Applications/Telegrams/CryptoTelegramService.cs`

**功能**：
- ✅ 获取虚拟币市场快讯（基于 CryptoCompare API）
- ✅ 标注重要新闻
- ✅ 提取相关币种符号

**API 使用**：
```
GET https://min-api.cryptocompare.com/data/v2/news/?lang=EN
```

**注意**: 使用免费的 CryptoCompare API，无需 API Key。

## 数据流程图

```
首页 (HomePageView)
  ├── 搜索 → CryptoHomeService.SearchAssetAsync()
  │            └── CryptoAssetInfoService.SearchAsync()
  │                 └── 币安 API: /api/v3/exchangeInfo
  │
  ├── 热门 → CryptoHomeService.GetHotAssetsAsync()
  │            └── CryptoAssetInfoService.GetHotAssetsAsync()
  │                 └── 币安 API: /api/v3/ticker/24hr
  │
  └── 最近 → CryptoHomeService.GetRecentAssets()
               └── CryptoHistoryService.GetHistory()
                    └── 本地存储 (Preferences)

资产详情页 (AssetPageView)
  ├── 基本信息 → CryptoAssetInfoService.GetAssetInfoAsync()
  │                └── 币安 API: /api/v3/ticker/24hr
  │
  └── K线图表 → CryptoChartService.GetKLineDataAsync()
                 └── CryptoKLineService.Get{Period}KLineDataAsync()
                      └── 币安 API: /api/v3/klines

收藏页 (FavoritesPageView)
  └── 收藏列表 → CryptoFavoriteService.GetFavoritesWithLatestDataAsync()
                   ├── 本地存储 (Preferences)
                   └── 批量获取最新数据 (并发请求)
```

## 技术特点

### 1. 无需 API Key ⭐
所有币安公开市场数据接口无需 API Key，直接访问。

### 2. 数据隔离 ⭐
- A股和虚拟币使用不同的存储 Key
- 收藏: `FavoriteAssets_AShare` vs `FavoriteAssets_Crypto`
- 历史: `RecentAssets_AShare` vs `RecentAssets_Crypto`

### 3. 实时数据 ⭐
- K线数据不使用缓存，确保实时性
- 交易对列表缓存 1 小时（变化频率低）

### 4. 友好的错误处理 ⭐
- 使用 `FriendlyException` 提供用户友好的错误信息
- 完整的日志记录
- 优雅降级（网络错误返回空列表）

### 5. 性能优化 ⭐
- 并发获取收藏列表的最新数据
- HTTP 超时设置（30秒）
- 智能数据格式化

## 使用示例

### 搜索虚拟币

```csharp
var assetInfoService = serviceProvider
    .GetRequiredKeyedService<IAssetInfoService>(MarketType.Crypto);

var results = await assetInfoService.SearchAsync("BTC");
// 返回: [(BTC, BTCUSDT), (BTC, BTCBUSD), ...]
```

### 获取K线数据

```csharp
var chartService = serviceProvider
    .GetRequiredKeyedService<IChartDataService>(MarketType.Crypto);

var klineData = await chartService.GetKLineDataAsync("BTCUSDT", KLineType.Daily, 100);
// 返回: 最近100天的日K线数据
```

### 添加到收藏

```csharp
var favoriteService = serviceProvider
    .GetRequiredKeyedService<IFavoriteService>(MarketType.Crypto);

favoriteService.AddFavorite("BTCUSDT", "");
// 虚拟币的 market 参数使用空字符串
```

## 限制与注意事项

### 币安 API 限流
- **请求频率**: 1200次/分钟（单IP）
- **权重限制**: 6000/分钟
- **建议**: 避免频繁请求，间隔 > 1 秒

### 数据格式
- **交易对**: 必须大写（BTCUSDT）
- **价格**: 字符串格式，需要解析为 decimal
- **时间戳**: Unix 毫秒，需要转换为 DateTime

### 网络要求
- **超时**: 30秒
- **重试**: 未实现，由调用方处理
- **代理**: 可通过 HttpClient 配置

## 未实现功能

以下服务虚拟币版本暂未实现（占位）：

- ❌ `CryptoScreenerService` - 资产筛选（需要更复杂的筛选逻辑）
- ❌ `CryptoCacheService` - 缓存服务（不需要，实时数据）
- ❌ Crypto Agent Tools - AI 工具集（需要额外开发）

## 测试建议

### 手动测试清单

- [ ] 搜索 "BTC" 能返回相关交易对
- [ ] 热门虚拟币能正常显示（8个）
- [ ] 点击虚拟币卡片能跳转详情页
- [ ] 详情页 K线图能正常显示
- [ ] 收藏功能能正常添加/移除
- [ ] 历史记录能正常记录和显示
- [ ] 市场切换后数据正确刷新

### 性能测试

- [ ] 搜索响应时间 < 2秒
- [ ] 热门列表加载时间 < 3秒
- [ ] K线数据加载时间 < 5秒
- [ ] 收藏列表刷新时间 < 10秒（8个并发）

## 参考文档

- [币安 REST API 文档](https://developers.binance.com/docs/zh-CN/binance-spot-api-docs/rest-api)
- [K线数据详细说明](./Charts/README_CRYPTO.md)
- [枚举定义](https://developers.binance.com/docs/zh-CN/binance-spot-api-docs/enums)
- [错误代码](https://developers.binance.com/docs/zh-CN/binance-spot-api-docs/error-codes)

## 更新日志

### v1.0.0 (2025-01-04)
- ✅ 完整实现 6 个核心服务
- ✅ 支持搜索、热门、详情、K线、收藏、历史
- ✅ 基于币安公开 API，无需认证
- ✅ 完整的错误处理和日志
- ✅ 编译通过，无错误

## 下一步

建议按以下顺序继续开发：

1. **UI 集成测试** - 确保虚拟币市场在UI层正常工作
2. **添加单元测试** - 为核心服务添加测试用例
3. **性能优化** - 根据实际使用情况调整
4. **完善 Agent Tools** - 实现虚拟币相关的 AI 工具集
5. **添加更多交易对** - 支持 BTC、ETH 等基础交易对

