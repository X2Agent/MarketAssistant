# PlaywrightService 重构说明

## 重构背景

原有实现存在以下问题：

1. **资源阻止策略导致请求挂起**：使用 `SafeExecuteAsync` 包装路由处理，吞掉异常导致请求超时
2. **性能问题**：每次调用都创建新的 BrowserContext 和 Page，频繁创建销毁影响性能
3. **错误处理不当**：`SafeExecuteAsync` 默默吞掉所有异常，隐藏实际问题

## 重构内容

### 1. 简化资源阻止策略

**之前**：
```csharp
await context.RouteAsync("**/*", async route =>
{
    var resourceType = route.Request.ResourceType;
    if (resourceType is "image" or "media" or "font")
    {
        await SafeExecuteAsync(() => route.AbortAsync());
    }
    else
    {
        await SafeExecuteAsync(() => route.ContinueAsync());
    }
});
```

**现在**：
```csharp
await context.RouteAsync("**/*", route =>
{
    try
    {
        var resourceType = route.Request.ResourceType;
        if (BlockedResourceTypes.Contains(resourceType))
        {
            return route.AbortAsync();
        }
        else
        {
            return route.ContinueAsync();
        }
    }
    catch (Exception ex)
    {
        _logger?.LogWarning(ex, "路由处理失败: {Url}", route.Request.Url);
        return route.ContinueAsync();
    }
});
```

**改进点**：
- 直接返回 Task，不使用 async/await，避免不必要的开销
- 移除 `SafeExecuteAsync` 包装，确保路由处理正确执行
- 异常时记录日志并继续请求，而不是吞掉异常

### 2. 移除不必要的健康检查

移除了 `PerformHealthCheckIfNeeded` 和相关的时间跟踪字段，简化代码逻辑。浏览器连接检查已在 `GetBrowserAsync` 中处理。

### 3. 简化初始化和清理逻辑

- 合并 `CreateBrowserOptions`、`InstallChromiumAsync` 等辅助方法到 `InitializeBrowserAsync`
- 统一清理逻辑到 `CleanupAsync` 方法
- 移除 `GracefulShutdownAsync`，保留标准的 `DisposeAsync`

### 4. 使用 C# 12 语法

- 使用集合表达式：`string[] arr = ["a", "b"]`
- 内联创建选项对象

## 性能优化

1. **并发控制**：保持最多 5 个并发页面的信号量控制
2. **浏览器重用**：单例模式的浏览器实例，避免重复创建
3. **资源过滤**：阻止图片、媒体和字体加载，减少带宽和加载时间

## 代码统计

- **减少代码行数**：从 324 行减少到 233 行（减少 28%）
- **减少方法数量**：从 12 个方法减少到 8 个方法
- **移除不必要的复杂度**：健康检查、SafeExecuteAsync 等

## 问题修复

### 问题 1：首页搜索没结果
**原因**：路由处理器中的 `SafeExecuteAsync` 吞掉异常，导致某些请求挂起超时  
**修复**：直接处理路由，确保每个请求都被正确 abort 或 continue

### 问题 2：股票收藏页数据加载慢
**原因**：虽然每次都创建新 context，但这是必要的隔离机制。性能问题主要在于并发控制  
**优化**：保持并发信号量控制，避免过多并发请求

## 使用示例

```csharp
// 搜索股票
var results = await _playwrightService.ExecuteWithPageAsync(async page =>
{
    await page.GotoAsync(url);
    await page.WaitForSelectorAsync(".search-results");
    var elements = await page.QuerySelectorAllAsync(".stock-item");
    return await ProcessElements(elements);
}, cancellationToken: cancellationToken);

// 获取股票信息（带自定义超时）
var stockInfo = await _playwrightService.ExecuteWithPageAsync(async page =>
{
    await page.GotoAsync(url);
    await page.WaitForSelectorAsync(".stock-detail");
    return await ExtractStockInfo(page);
}, timeout: TimeSpan.FromSeconds(15), cancellationToken: cancellationToken);
```

## 测试建议

1. 测试首页搜索功能，确认能正常返回结果
2. 测试股票收藏页，确认数据加载速度
3. 并发测试：同时触发多个搜索/数据获取请求
4. 异常测试：网络超时、页面不存在等场景


