# MarketAssistant 从 Semantic Kernel 迁移到 Microsoft Agent Framework 计划

## 概述

本文档详细描述了将 MarketAssistant 项目从 Semantic Kernel (SK) 迁移到 Microsoft Agent Framework 的计划和步骤。Microsoft Agent Framework 是 Semantic Kernel 的演进版本，提供了更现代化的 API 和更好的开发体验。

## 当前项目分析

### 项目结构
MarketAssistant 是一个基于 Avalonia UI 的桌面应用程序，主要功能包括：
- 股票市场分析
- AI 对话和分析
- 新闻聚合
- 股票选择

### 当前使用的 Semantic Kernel 组件

1. **核心包依赖**
   - Microsoft.SemanticKernel (1.65.0)
   - Microsoft.SemanticKernel.Agents.Core (1.65.0)
   - Microsoft.SemanticKernel.Connectors.OpenAI (1.65.0)
   - Microsoft.SemanticKernel.PromptTemplates.Handlebars (1.65.0)
   - Microsoft.SemanticKernel.Rag (1.65.0-preview)

2. **主要使用场景**
   - Agent 实现 (MarketAnalysisAgent, MarketChatSession)
   - 插件系统 (StockBasicPlugin, GroundingSearchPlugin)
   - 过滤器 (PromptCacheFilter, PromptCacheWriteFilter)
   - Kernel 工厂 (KernelFactory)
   - 对话管理 (ChatHistory)
   - 向量存储和 RAG

3. **关键类和接口**
   - Kernel
   - ChatCompletionAgent
   - KernelPlugin
   - KernelFunction
   - ChatHistory
   - IPromptRenderFilter
   - IFunctionInvocationFilter
   - IAutoFunctionInvocationFilter

## 迁移计划

### 阶段 1: 准备工作

1. **创建备份**
   - 创建当前代码库的完整备份
   - 创建新的分支进行迁移工作

2. **更新项目依赖**
   - 移除 Semantic Kernel 相关包
   - 添加 Microsoft Agent Framework 相关包
   - 更新项目文件中的包引用

### 阶段 2: 核心组件迁移

1. **迁移 Kernel 到 Agent**
   - 替换 Kernel 实例为 Agent 实例
   - 更新 KernelFactory 为 AgentFactory
   - 更新依赖注入配置

2. **迁移 Agent 实现**
   - 更新 MarketAnalysisAgent 使用新的 Agent API
   - 更新 MarketChatSession 使用新的 Agent API
   - 更新 AnalystManager 使用新的 Agent API

3. **迁移插件系统**
   - 更新插件接口和实现
   - 更新插件注册方式
   - 测试插件功能

### 阶段 3: 高级功能迁移

1. **迁移对话管理**
   - 更新 ChatHistory 使用新的 API
   - 更新对话处理逻辑

2. **迁移过滤器系统**
   - 更新过滤器接口
   - 更新过滤器实现
   - 更新过滤器注册方式

3. **迁移 RAG 和向量存储**
   - 更新向量存储 API
   - 更新 RAG 实现

### 阶段 4: 测试和优化

1. **单元测试**
   - 为迁移后的组件编写单元测试
   - 运行测试确保功能正确

2. **集成测试**
   - 测试整体功能
   - 测试用户交互场景

3. **性能优化**
   - 分析性能影响
   - 优化关键路径

## 详细迁移步骤

### 步骤 1: 更新项目依赖

#### 移除的包
```xml
<PackageReference Include="Microsoft.SemanticKernel" Version="1.65.0" />
<PackageReference Include="Microsoft.SemanticKernel.Agents.Core" Version="1.65.0" />
<PackageReference Include="Microsoft.SemanticKernel.Connectors.OpenAI" Version="1.65.0" />
<PackageReference Include="Microsoft.SemanticKernel.PromptTemplates.Handlebars" Version="1.65.0" />
<PackageReference Include="Microsoft.SemanticKernel.Rag" Version="1.65.0-preview" />
```

#### 添加的包
```xml
<PackageReference Include="Microsoft.Agents.Core" Version="1.0.1" />
<PackageReference Include="Microsoft.Agents.Builder" Version="1.0.1" />
<PackageReference Include="Microsoft.Agents.Client" Version="1.0.1" />
<PackageReference Include="Microsoft.Agents.AI" Version="1.0.1-preview" />
<PackageReference Include="Microsoft.Agents.AI.Abstractions" Version="1.0.1-preview" />
```

### 步骤 2: 更新命名空间

#### 命名空间映射
| Semantic Kernel                         | Microsoft Agent Framework                     |
| --------------------------------------- | --------------------------------------------- |
| Microsoft.SemanticKernel                | Microsoft.Extensions.AI / Microsoft.Agents.AI |
| Microsoft.SemanticKernel.Agents         | Microsoft.Agents.AI                           |
| Microsoft.SemanticKernel.ChatCompletion | Microsoft.Extensions.AI                       |
| Microsoft.SemanticKernel.Plugins        | Microsoft.Extensions.AI                       |
| Microsoft.SemanticKernel.Rag            | Microsoft.Agents.Rag (待确认)                 |

**注意**：Agent Framework 使用 `Microsoft.Extensions.AI` 作为核心抽象层，`Microsoft.Agents.AI` 提供具体实现。

### 步骤 3: 迁移 Kernel 到 Agent

#### KernelFactory 迁移到 AgentFactory

**原始代码 (KernelFactory.cs):**
```csharp
public class KernelFactory : IKernelFactory
{
    private readonly IKernelPluginConfig _kernelPluginConfig;
    private readonly IUserSettingService _userSettingService;

    public KernelFactory(IKernelPluginConfig kernelPluginConfig, IUserSettingService userSettingService)
    {
        _kernelPluginConfig = kernelPluginConfig;
        _userSettingService = userSettingService;
    }

    public Kernel CreateKernel()
    {
        var builder = Kernel.CreateBuilder();
        
        // 配置 OpenAI 聊天完成服务
        builder.AddOpenAIChatCompletion(
            modelId: _userSettingService.CurrentSetting.Model,
            apiKey: _userSettingService.CurrentSetting.ApiKey,
            endpoint: _userSettingService.CurrentSetting.Endpoint);
            
        var kernel = builder.Build();
        
        // 加载插件
        kernel.Plugins.AddFromType<ConversationSummaryPlugin>();
        kernel.Plugins.AddFromType<TimePlugin>();
        kernel.Plugins.AddFromType<GroundingSearchPlugin>();
        
        return kernel;
    }
}
```

**迁移后代码 (ChatClientFactory.cs):**
```csharp
using Microsoft.Extensions.AI;
using OpenAI;

public class ChatClientFactory : IChatClientFactory
{
    private readonly IUserSettingService _userSettingService;
    private readonly object _lock = new();
    private IChatClient? _cached;
    private string? _lastError;

    public ChatClientFactory(IUserSettingService userSettingService)
    {
        _userSettingService = userSettingService;
    }

    public IChatClient CreateChatClient()
    {
        if (TryCreateChatClient(out var client, out var error)) return client;
        throw new FriendlyException(error);
    }

    public bool TryCreateChatClient(out IChatClient client, out string error)
    {
        lock (_lock)
        {
            if (_cached != null)
            {
                client = _cached;
                error = string.Empty;
                return true;
            }
            if (!string.IsNullOrEmpty(_lastError))
            {
                client = null!;
                error = _lastError!;
                return false;
            }
            try
            {
                _cached = Build();
                client = _cached;
                error = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                client = null!;
                error = _lastError;
                return false;
            }
        }
    }

    public void Invalidate()
    {
        lock (_lock)
        {
            _cached = null;
            _lastError = null;
        }
    }

    private IChatClient Build()
    {
        var userSetting = _userSettingService.CurrentSetting;
        if (string.IsNullOrWhiteSpace(userSetting.ModelId))
            throw new FriendlyException("AI 功能未配置：请先在设置页面选择 AI 模型");
        if (string.IsNullOrWhiteSpace(userSetting.ApiKey))
            throw new FriendlyException("AI 功能未配置：请先在设置页面配置 API Key");
        if (string.IsNullOrWhiteSpace(userSetting.Endpoint))
            throw new FriendlyException("AI 功能未配置：请先在设置页面配置 API 端点");

        // 创建 OpenAI 客户端
        var openAIClient = new OpenAIClient(userSetting.ApiKey, new OpenAIClientOptions
        {
            Endpoint = new Uri(userSetting.Endpoint)
        });
        
        // 获取 ChatClient（实现了 IChatClient）
        var chatClient = openAIClient.AsChatClient(userSetting.ModelId);
        
        return chatClient;
    }
}
```

**重要变更说明**：
1. Agent Framework 使用 `IChatClient` 作为核心抽象，不再是 Kernel
2. 工具/插件在创建 AIAgent 时通过 `tools` 参数传递
3. 使用 OpenAI SDK 的 `AsChatClient()` 扩展方法获取 IChatClient
4. 不再使用 Builder 模式配置插件，而是在创建 Agent 时直接传递

### 步骤 4: 迁移 Agent 实现

#### MarketAnalysisAgent 迁移

**原始代码 (MarketAnalysisAgent.cs):**
```csharp
public class MarketAnalysisAgent
{
    private readonly Kernel _kernel;
    private readonly IKernelPluginConfig _kernelPluginConfig;
    private readonly ILogger<MarketAnalysisAgent> _logger;

    public MarketAnalysisAgent(Kernel kernel, IKernelPluginConfig kernelPluginConfig, ILogger<MarketAnalysisAgent> logger)
    {
        _kernel = kernel;
        _kernelPluginConfig = kernelPluginConfig;
        _logger = logger;
    }

    public async Task<string> AnalysisAsync(string symbol, string query)
    {
        var plugin = _kernel.Plugins["StockBasicPlugin"];
        var function = plugin["GetStockInfo"];
        
        var result = await function.InvokeAsync(_kernel, new KernelArguments
        {
            ["symbol"] = symbol
        });
        
        var stockInfo = result.GetValue<string>();
        
        var analysisPrompt = $@"
        分析以下股票信息:
        {stockInfo}
        
        用户查询: {query}
        
        请提供详细的分析报告。
        ";
        
        var analysisResult = await _kernel.InvokePromptAsync(analysisPrompt);
        
        return analysisResult.GetValue<string>() ?? string.Empty;
    }
}
```

**迁移后代码 (MarketAnalysisAgent.cs):**
```csharp
using Microsoft.Extensions.AI;

public class MarketAnalysisAgent
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<MarketAnalysisAgent> _logger;

    public MarketAnalysisAgent(IChatClient chatClient, ILogger<MarketAnalysisAgent> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<string> AnalysisAsync(string symbol, string query)
    {
        // Agent Framework 中，工具在创建 AIAgent 时注册
        // 然后通过对话消息触发工具调用
        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.User, $"分析股票 {symbol}：{query}")
        };
        
        var response = await _chatClient.CompleteAsync(messages);
        
        return response.Message.Text ?? string.Empty;
    }
}
```

**重要变更说明**：
1. 不再直接操作插件，而是通过对话触发工具调用
2. 使用 `IChatClient.CompleteAsync()` 替代 `Kernel.InvokePromptAsync()`
3. 工具调用由 AI 模型自动决定，无需手动调用插件方法

### 步骤 5: 迁移插件系统

#### StockBasicPlugin 迁移

**原始代码 (StockBasicPlugin.cs):**
```csharp
public class StockBasicPlugin
{
    private readonly StockService _stockService;
    private readonly ILogger<StockBasicPlugin> _logger;

    public StockBasicPlugin(StockService stockService, ILogger<StockBasicPlugin> logger)
    {
        _stockService = stockService;
        _logger = logger;
    }

    [KernelFunction]
    [Description("获取股票基本信息")]
    public async Task<string> GetStockInfo(string symbol)
    {
        try
        {
            var stock = await _stockService.GetStockInfoAsync(symbol);
            return JsonSerializer.Serialize(stock);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取股票信息失败: {Symbol}", symbol);
            return $"获取股票信息失败: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("获取股票K线数据")]
    public async Task<string> GetStockKLine(string symbol, string period = "1d", int count = 20)
    {
        try
        {
            var kLines = await _stockService.GetStockKLineAsync(symbol, period, count);
            return JsonSerializer.Serialize(kLines);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取股票K线数据失败: {Symbol}", symbol);
            return $"获取股票K线数据失败: {ex.Message}";
        }
    }
}
```

**迁移后代码 (StockBasicPlugin.cs):**
```csharp
public class StockBasicPlugin
{
    private readonly StockService _stockService;
    private readonly ILogger<StockBasicPlugin> _logger;

    public StockBasicPlugin(StockService stockService, ILogger<StockBasicPlugin> logger)
    {
        _stockService = stockService;
        _logger = logger;
    }

    [AgentFunction]
    [Description("获取股票基本信息")]
    public async Task<string> GetStockInfo(string symbol)
    {
        try
        {
            var stock = await _stockService.GetStockInfoAsync(symbol);
            return JsonSerializer.Serialize(stock);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取股票信息失败: {Symbol}", symbol);
            return $"获取股票信息失败: {ex.Message}";
        }
    }

    [AgentFunction]
    [Description("获取股票K线数据")]
    public async Task<string> GetStockKLine(string symbol, string period = "1d", int count = 20)
    {
        try
        {
            var kLines = await _stockService.GetStockKLineAsync(symbol, period, count);
            return JsonSerializer.Serialize(kLines);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取股票K线数据失败: {Symbol}", symbol);
            return $"获取股票K线数据失败: {ex.Message}";
        }
    }
}
```

### 步骤 6: 迁移过滤器系统

#### PromptCacheFilter 迁移

**原始代码 (PromptCacheFilter.cs):**
```csharp
public class PromptCacheFilter : IPromptRenderFilter
{
    private readonly ILogger<PromptCacheFilter> _logger;
    private readonly IVectorStore _vectorStore;

    public PromptCacheFilter(ILogger<PromptCacheFilter> logger, IVectorStore vectorStore)
    {
        _logger = logger;
        _vectorStore = vectorStore;
    }

    public async Task OnPromptRenderAsync(PromptRenderContext context, Func<PromptRenderContext, Task> next)
    {
        // 检查缓存
        var cacheKey = GenerateCacheKey(context);
        var cachedResult = await GetFromCache(cacheKey);
        
        if (cachedResult != null)
        {
            context.RenderedPrompt = cachedResult;
            return;
        }
        
        // 执行下一个过滤器
        await next(context);
        
        // 保存到缓存
        await SaveToCache(cacheKey, context.RenderedPrompt);
    }
    
    // ... 其他方法
}
```

**迁移后代码 (PromptCacheFilter.cs):**
```csharp
public class PromptCacheFilter : IPromptRenderFilter
{
    private readonly ILogger<PromptCacheFilter> _logger;
    private readonly IVectorStore _vectorStore;

    public PromptCacheFilter(ILogger<PromptCacheFilter> logger, IVectorStore vectorStore)
    {
        _logger = logger;
        _vectorStore = vectorStore;
    }

    public async Task OnPromptRenderAsync(PromptRenderContext context, Func<PromptRenderContext, Task> next)
    {
        // 检查缓存
        var cacheKey = GenerateCacheKey(context);
        var cachedResult = await GetFromCache(cacheKey);
        
        if (cachedResult != null)
        {
            context.RenderedPrompt = cachedResult;
            return;
        }
        
        // 执行下一个过滤器
        await next(context);
        
        // 保存到缓存
        await SaveToCache(cacheKey, context.RenderedPrompt);
    }
    
    // ... 其他方法
}
```

### 步骤 7: 更新依赖注入配置

#### ServiceCollectionExtensions 迁移

**原始代码 (ServiceCollectionExtensions.cs):**
```csharp
// 注册 Kernel 和嵌入服务
services.AddSingleton<IKernelFactory, KernelFactory>();
services.AddSingleton<IEmbeddingFactory, EmbeddingFactory>();
services.AddSingleton<IKernelPluginConfig, KernelPluginConfig>();

services.AddSingleton(serviceProvider =>
{
    var svc = serviceProvider.GetRequiredService<IKernelFactory>();
    return svc.CreateKernel();
});

// 注册 Kernel 过滤器
services.AddSingleton<IFunctionInvocationFilter, FunctionInvocationLoggingFilter>();
services.AddSingleton<IPromptRenderFilter, PromptRenderLoggingFilter>();
services.AddSingleton<IAutoFunctionInvocationFilter, AutoFunctionInvocationLoggingFilter>();
services.AddSingleton<IPromptRenderFilter, PromptCacheFilter>();
services.AddSingleton<IFunctionInvocationFilter, PromptCacheWriteFilter>();
```

**迁移后代码 (ServiceCollectionExtensions.cs):**
```csharp
// 注册 Agent 和嵌入服务
services.AddSingleton<IAgentFactory, AgentFactory>();
services.AddSingleton<IEmbeddingFactory, EmbeddingFactory>();
services.AddSingleton<IAgentPluginConfig, AgentPluginConfig>();

services.AddSingleton(serviceProvider =>
{
    var svc = serviceProvider.GetRequiredService<IAgentFactory>();
    return svc.CreateAgent();
});

// 注册 Agent 过滤器
services.AddSingleton<IFunctionInvocationFilter, FunctionInvocationLoggingFilter>();
services.AddSingleton<IPromptRenderFilter, PromptRenderLoggingFilter>();
services.AddSingleton<IAutoFunctionInvocationFilter, AutoFunctionInvocationLoggingFilter>();
services.AddSingleton<IPromptRenderFilter, PromptCacheFilter>();
services.AddSingleton<IFunctionInvocationFilter, PromptCacheWriteFilter>();
```

## 注意事项

1. **API 变化**: Microsoft Agent Framework 的 API 与 Semantic Kernel 有所不同，需要仔细参考官方文档进行调整。

2. **兼容性**: 确保所有依赖项与新框架兼容，特别是第三方插件和扩展。

3. **测试**: 迁移后进行全面测试，确保所有功能正常工作。

4. **性能**: 监控迁移后的性能，确保没有性能回归。

5. **文档**: 更新项目文档，反映新的架构和 API 使用方式。

## 时间表

- 第1周: 准备工作和依赖更新
- 第2周: 核心组件迁移
- 第3周: 高级功能迁移
- 第4周: 测试和优化

## 资源

- [Microsoft Agent Framework 官方文档](https://learn.microsoft.com/en-us/agent-framework/)
- [从 Semantic Kernel 迁移指南](https://learn.microsoft.com/en-us/agent-framework/migration-guide/from-semantic-kernel/?pivots=programming-language-csharp)
- [Microsoft Agent Framework API 参考](https://learn.microsoft.com/en-us/dotnet/api/microsoft.agents)