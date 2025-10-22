# MCP 服务管理

本目录包含 Model Context Protocol (MCP) 相关的服务管理代码。

## 概述

`McpService` 是一个统一的 MCP 服务，用于处理 MCP 客户端的创建、连接和工具加载。这个类消除了之前在 `MarketChatSession.InitializeMcpServicesAsync()` 和 `McpPlugin.GetKernelFunctionsAsync()` 之间存在的重复代码逻辑。

## 主要功能

### 1. 统一的传输创建

支持三种传输类型：
- **Stdio**: 标准输入输出传输（本地进程）
- **SSE**: Server-Sent Events 传输（HTTP 流）
- **StreamableHttp**: 可流式传输的 HTTP

### 2. 双框架支持

`McpService` 同时支持：
- **Agent Framework**: 返回 `AITool` 列表，用于新的 Microsoft Agent Framework
- **Semantic Kernel**: 返回 `KernelFunction` 列表，用于 Semantic Kernel

### 3. 生命周期管理

- 自动管理 MCP 客户端的生命周期
- 实现 `IAsyncDisposable` 接口，确保资源正确释放
- 支持可选的客户端生命周期管理

## 使用示例

### 用于 Agent Framework

```csharp
var service = new McpService(logger);
var configs = McpService.GetEnabledConfigs();
var tools = await service.GetAIToolsAsync(configs, manageClientLifetime: true);

// 使用工具
var chatOptions = new ChatOptions
{
    Tools = tools,
    Temperature = 0.7f
};

// 清理
await service.DisposeAsync();
```

### 用于 Semantic Kernel

```csharp
var service = new McpService();
var configs = McpService.GetEnabledConfigs();
var functions = await service.GetKernelFunctionsAsync(configs);

// 函数会自动添加到 Kernel
// 客户端会在函数创建后自动释放
```

### 创建自定义传输

```csharp
var config = new MCPServerConfig
{
    Name = "my-server",
    TransportType = "stdio",
    Command = "npx",
    Arguments = "-y @modelcontextprotocol/server-filesystem",
    EnvironmentVariables = new Dictionary<string, string>()
};

var transport = McpService.CreateClientTransport(config);
```

## 设计决策

### 为什么需要 McpService？

之前的实现存在以下问题：
1. **代码重复**: `MarketChatSession` 和 `McpPlugin` 中有大量相同的代码
2. **维护困难**: 修改 MCP 连接逻辑需要同时修改多个地方
3. **缺乏统一管理**: 没有统一的地方管理 MCP 客户端生命周期

### 解决方案

`McpService` 提供：
- 统一的传输创建逻辑
- 统一的客户端管理
- 灵活的工具获取接口（支持两种框架）
- 完善的资源释放机制

## 相关文档

- [Model Context Protocol 规范](https://modelcontextprotocol.io/)
- [Microsoft Agent Framework 文档](https://learn.microsoft.com/zh-cn/agent-framework/)
- [Semantic Kernel 文档](https://learn.microsoft.com/semantic-kernel/)

## 变更历史

### 2025-10-20
- 创建 `McpService` 类（原名 `McpManager`）
- 删除 `McpPlugin`，直接使用 `McpService`
- 重构 `MarketChatSession` 使用 `McpService`
- 消除重复代码，提高可维护性
- 统一命名规范，与项目整体风格保持一致

### 2025-01-XX
- **重命名**: `MarketChatAgent` → `MarketChatSession`
- 更准确地反映其作为对话会话管理器的职责

