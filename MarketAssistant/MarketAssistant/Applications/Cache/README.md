# 分析结果缓存服务

## 概述

分析结果缓存服务 (`AnalysisCacheService`) 提供智能缓存机制，避免短时间内重复分析同一只股票，显著提升用户体验和系统性能。

## 主要特性

### 🚀 智能缓存策略
- **简化缓存键**: 基于股票代码和日期生成唯一缓存键
- **内存缓存**: 使用 .NET IMemoryCache，提供高性能缓存机制
- **自动过期**: 可配置的缓存过期时间，默认2小时
- **自动LRU淘汰**: 内存压力时自动清理最少使用的缓存项

### 📊 缓存管理
- **自动过期**: IMemoryCache 自动管理缓存过期和清理
- **内存压力感知**: 系统内存不足时自动释放缓存
- **统计信息**: 提供缓存命中率、使用情况等统计数据

### 🔧 灵活配置
- **缓存时长**: 可配置缓存过期时间（默认2小时）
- **内存限制**: 可设置最大缓存大小（默认50MB）

## 使用方式

### 基本用法

```csharp
// 获取缓存的分析结果
var cachedResult = await cacheService.GetCachedAnalysisAsync("AAPL");
if (cachedResult != null)
{
    // 使用缓存结果
    return cachedResult;
}

// 执行新的分析
var analysisResult = await PerformAnalysis("AAPL");

// 缓存分析结果
await cacheService.CacheAnalysisAsync("AAPL", analysisResult);
```

### 缓存清理

```csharp
// 清除特定股票的缓存
await cacheService.ClearCacheAsync("AAPL");

```


## 配置选项

```csharp
// 配置 IMemoryCache
var memoryCache = new MemoryCache(new MemoryCacheOptions 
{
    SizeLimit = 50 * 1024 * 1024 // 50MB 限制
});

// 创建缓存服务，指定过期时间
var cacheService = new AnalysisCacheService(logger, memoryCache, TimeSpan.FromHours(2));
```

## 集成到MarketAnalysisAgent

缓存服务已自动集成到 `MarketAnalysisAgent` 中：

```csharp
// 分析时自动检查缓存
var result = await marketAnalysisAgent.AnalysisAsync("AAPL");

// 强制刷新，忽略缓存
var freshResult = await marketAnalysisAgent.AnalysisAsync("AAPL", forceRefresh: true);
```

## 缓存键生成规则

缓存键格式：`{股票代码}_{分析类型}_{日期}`

示例：
- `AAPL_default_20241213` - AAPL的默认分析
- `TSLA_technical_20241213` - TSLA的技术分析
- `MSFT_fundamental_20241213` - MSFT的基本面分析

## 性能优化

### 内存使用
- 使用 `IMemoryCache` 提供线程安全和高性能缓存
- 自动LRU淘汰策略，优化内存使用
- 内存压力感知，自动释放不常用缓存
- 支持大小限制，防止内存溢出

## 监控和调试

### 日志记录
- 缓存命中/未命中事件
- 缓存保存/清理操作
- 错误和警告信息


## 注意事项

1. **缓存一致性**: 缓存基于日期，同一天内的分析结果会被复用
2. **内存限制**: IMemoryCache 受配置的大小限制约束
3. **并发安全**: IMemoryCache 内部已处理并发访问，外部调用无需额外同步
4. **错误处理**: 缓存操作失败不会影响正常的分析流程
5. **自动清理**: IMemoryCache 自动处理过期缓存，无需手动干预

## 未来改进

- [x] 实现LRU缓存淘汰策略（已通过IMemoryCache实现）
- [x] 移除磁盘持久化，简化实现
- [x] 改进缓存命中率统计
- [ ] 支持缓存预热功能
- [ ] 添加缓存压缩选项
- [ ] 实现分布式缓存支持
- [ ] 提供缓存管理UI界面

