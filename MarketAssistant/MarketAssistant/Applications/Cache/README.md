# 分析结果缓存服务

## 概述

分析结果缓存服务 (`AnalysisCacheService`) 提供智能缓存机制，避免短时间内重复分析同一只股票，显著提升用户体验和系统性能。

## 架构设计

### 缓存层级
- **ViewModel层调用**: 缓存服务在 `AgentAnalysisViewModel` 中调用，而不是在 `MarketAnalysisAgent` 中
- **缓存对象**: 直接缓存解析后的 `AnalystResult` 对象，而不是原始的 `ChatHistory`
- **数据流向**: 分析完成 → 解析结果 → 缓存结果 → UI展示

### 主要特性

#### 🚀 智能缓存策略
- **简化缓存键**: 基于股票代码和日期生成唯一缓存键
- **内存缓存**: 使用 .NET IMemoryCache，提供高性能缓存机制
- **自动过期**: 可配置的缓存过期时间，默认2小时
- **自动LRU淘汰**: 内存压力时自动清理最少使用的缓存项

#### 📊 缓存管理
- **自动过期**: IMemoryCache 自动管理缓存过期和清理
- **内存压力感知**: 系统内存不足时自动释放缓存
- **结构化缓存**: 缓存完整的 `AnalystResult` 对象，包含所有分析数据

#### 🔧 灵活配置
- **缓存时长**: 可配置缓存过期时间（默认2小时）
- **内存限制**: 可设置最大缓存大小（默认50MB）

## 使用方式

### ViewModel层集成

缓存服务在 `AgentAnalysisViewModel` 中集成，提供完整的缓存生命周期管理：

```csharp
public class AgentAnalysisViewModel : ViewModelBase
{
    private readonly IAnalysisCacheService _analysisCacheService;
    
    public async Task LoadAnalysisDataAsync()
    {
        // 1. 首先尝试从缓存获取
        var cachedResult = await _analysisCacheService.GetCachedAnalysisAsync(StockCode);
        if (cachedResult != null)
        {
            // 使用缓存结果直接更新UI
            AnalysisReportViewModel.LoadCachedResult(cachedResult);
            return;
        }

        // 2. 缓存中没有，执行新分析
        var history = await _marketAnalysisAgent.AnalysisAsync(StockCode);
        // ... 处理分析结果
    }
    
    private async Task CacheAnalysisResultAsync()
    {
        // 3. 分析完成后自动缓存结果
        var analysisResult = AnalysisReportViewModel.GetCurrentAnalysisResult();
        if (analysisResult != null)
        {
            await _analysisCacheService.CacheAnalysisAsync(StockCode, analysisResult);
        }
    }
}
```

### 基本API使用

```csharp
// 获取缓存的分析结果
var cachedResult = await cacheService.GetCachedAnalysisAsync("AAPL");
if (cachedResult != null)
{
    // cachedResult 是完整的 AnalystResult 对象
    Console.WriteLine($"股票: {cachedResult.StockSymbol}");
    Console.WriteLine($"评级: {cachedResult.Rating}");
    Console.WriteLine($"目标价: {cachedResult.TargetPrice}");
}

// 缓存分析结果
var analysisResult = new AnalystResult
{
    StockSymbol = "AAPL",
    Rating = "买入",
    TargetPrice = "180-200美元",
    // ... 其他属性
};
await cacheService.CacheAnalysisAsync("AAPL", analysisResult);

// 清除特定股票的缓存
await cacheService.ClearCacheAsync("AAPL");
```

## 配置选项

```csharp
// 在 MauiProgramExtensions.cs 中配置
services.AddMemoryCache(options =>
{
    options.SizeLimit = 50 * 1024 * 1024; // 50MB 限制
});

services.AddScoped<IAnalysisCacheService, AnalysisCacheService>();
```

## 架构优势

### 为什么在ViewModel层缓存？

1. **更好的责任分离**: `MarketAnalysisAgent` 专注于分析逻辑，不需要关心缓存
2. **缓存有意义的数据**: 缓存解析后的 `AnalystResult`，而不是原始的 `ChatHistory`
3. **UI响应更快**: 缓存命中时可以直接更新UI，无需重新解析
4. **更灵活的缓存策略**: ViewModel层可以决定何时使用缓存，何时强制刷新

### 数据流向

```
用户请求 → AgentAnalysisViewModel.LoadAnalysisDataAsync()
    ↓
检查缓存 → IAnalysisCacheService.GetCachedAnalysisAsync()
    ↓ (缓存命中)
直接更新UI ← AnalysisReportViewModel.LoadCachedResult()

    ↓ (缓存未命中)
执行分析 → MarketAnalysisAgent.AnalysisAsync()
    ↓
解析结果 → AnalysisReportViewModel.ProcessAnalysisMessageAsync()
    ↓
缓存结果 → IAnalysisCacheService.CacheAnalysisAsync()
    ↓
更新UI ← AnalysisReportViewModel更新属性
```
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

