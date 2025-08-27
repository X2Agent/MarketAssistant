# 重排服务装饰器模式使用指南

## 概述

通过装饰器模式重构，现在的重排服务具有了更好的降级机制，解耦了服务之间的直接依赖关系。

## 架构设计

### 核心组件

1. **IRerankerService** - 重排服务接口
2. **OnnxCrossEncoderRerankerService** - ONNX 模型重排服务（主要）
3. **RerankerService** - 基础重排服务（备用）
4. **FallbackRerankerService** - 装饰器，负责降级逻辑

### 服务注册

```csharp
// 装饰器模式注册
services.AddSingleton<RerankerService>();
services.AddKeyedSingleton<IRerankerService, OnnxCrossEncoderRerankerService>("primary");
services.AddKeyedSingleton<IRerankerService, RerankerService>("fallback");
services.AddSingleton<IRerankerService, FallbackRerankerService>();
```

## 工作流程

1. **直接尝试主要服务**：优先使用 ONNX Cross-Encoder 重排服务
2. **异常降级**：如果主要服务失败，自动降级到备用服务
3. **兜底保护**：如果所有服务都失败，返回原始排序

## 使用方式

### 基本使用

```csharp
public class MyService
{
    private readonly IRerankerService _reranker;
    
    public MyService(IRerankerService reranker) // 自动注入 FallbackRerankerService
    {
        _reranker = reranker;
    }
    
    public async Task<IReadOnlyList<TextSearchResult>> ProcessResults(
        string query, 
        IEnumerable<TextSearchResult> results)
    {
        // 自动处理降级逻辑，无需关心具体实现
        return await _reranker.RerankAsync(query, results);
    }
}
```

### 环境变量配置

ONNX 服务需要以下环境变量：

- `CROSS_ENCODER_ONNX` - ONNX 模型文件路径
- `CROSS_ENCODER_VOCAB` - 词汇表文件路径  
- `CROSS_ENCODER_MAXLEN` - 最大序列长度（可选，默认256）

## 优势

- ✅ **解耦**：服务之间无直接依赖
- ✅ **简洁设计**：无额外接口，代码更简洁
- ✅ **自动降级**：透明的故障处理
- ✅ **易于测试**：可独立测试各组件
- ✅ **扩展性**：易于添加新的重排策略

## 日志输出

装饰器会记录详细的降级日志：

- `Debug`: "Primary reranker succeeded" - 主要服务成功
- `Warning`: "Primary reranker failed, falling back to secondary reranker" - 主要服务异常，降级
- `Error`: "Fallback reranker also failed" - 所有服务都失败

通过这种设计，你可以在不修改任何业务代码的情况下，享受到智能的服务降级和健壮的错误处理。
