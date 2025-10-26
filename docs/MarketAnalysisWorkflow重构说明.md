# MarketAnalysisWorkflow 重构说明

## 重构目标

将 `AnalystManager` 和 `MarketAnalysisAgent` 从旧的 `ConcurrentOrchestration`（Semantic Kernel Agents 预览版）迁移到新的 **Agent Framework Workflows 并发工作流**架构。

参考文档：[Microsoft Agent Framework 并发工作流教程](https://learn.microsoft.com/zh-cn/agent-framework/tutorials/workflows/simple-concurrent-workflow?pivots=programming-language-csharp)

## 重构架构

### 工作流模式：Fan-Out / Fan-In

```
用户请求（股票代码）
    ↓
[AnalysisDispatcher]  ← 步骤1：分发分析请求
    ↓ ↓ ↓ ↓ ↓
[财务分析师] [技术分析师] [新闻分析师] [基本面分析师] [情绪分析师]  ← 步骤2：并发分析
    ↓ ↓ ↓ ↓ ↓
[AnalysisAggregator]  ← 步骤3：聚合各分析师结果
    ↓
[CoordinatorExecutor] ← 步骤4：生成综合报告
    ↓
MarketAnalysisReport（最终报告）
```

## 已完成的工作

### 1. 目录结构
```
src/Agents/MarketAnalysis/
├── MarketAnalysisWorkflow.cs          # 主工作流类
├── Executors/
│   ├── AnalysisDispatcherExecutor.cs  # 分发器
│   ├── AnalysisAggregatorExecutor.cs  # 聚合器
│   └── CoordinatorExecutor.cs         # 协调器/总结器
└── Models/
    └── MarketAnalysisModels.cs        # 数据模型
```

### 2. 核心组件

#### AnalysisDispatcherExecutor
- **功能**：将分析请求分发到所有启用的分析师
- **输入**：`MarketAnalysisRequest`
- **输出**：广播 `ChatMessage` 到所有分析师

#### AnalysisAggregatorExecutor
- **功能**：收集并聚合各分析师的结果
- **输入**：多个 `ChatMessage`（来自各分析师）
- **输出**：`AggregatedAnalysisResult`

#### CoordinatorExecutor
- **功能**：基于所有分析师结果生成综合报告
- **输入**：`AggregatedAnalysisResult`
- **输出**：`MarketAnalysisReport`

#### MarketAnalysisWorkflow
- **功能**：组装整个工作流，管理分析师代理
- **特点**：
  - 根据用户设置动态创建分析师
  - 使用 `WorkflowBuilder` 构建 Fan-Out/Fan-In 结构
  - 提供进度事件通知

### 3. 更新的文件

- ✅ `src/Agents/MarketAnalysisAgent.cs` - 重构为使用新工作流
- ✅ `src/Services/ServiceCollectionExtensions.cs` - 添加工作流服务注册
- ✅ `tests/BaseKernelTest.cs` - 添加测试服务注册
- ✅ `tests/MarketAnalysisWorkflowTest.cs` - 创建工作流测试

## 当前待解决问题

### API 兼容性问题

由于 Agent Framework 使用 `Microsoft.Extensions.AI` 命名空间，而 Semantic Kernel 使用自己的类型，存在以下类型不兼容：

1. **ChatMessage 类型差异**
   - `Microsoft.Extensions.AI.ChatMessage` 
   - `Microsoft.SemanticKernel.ChatMessageContent`
   
2. **Role 枚举差异**
   - `Microsoft.Extensions.AI.ChatRole`
   - `Microsoft.SemanticKernel.ChatCompletion.AuthorRole`

3. **IChatClient API 差异**
   - Extensions.AI 的 `IChatClient` 没有 `CompleteAsync` 方法
   - 需要使用 `CompleteAsync<TResponse>` 或其他变体

### 解决方案选项

#### 选项 1：类型转换适配器（推荐）
创建适配器类在两种类型系统之间转换：

```csharp
public static class ChatMessageAdapter
{
    public static Microsoft.SemanticKernel.ChatMessageContent ToSemanticKernel(
        this Microsoft.Extensions.AI.ChatMessage message)
    {
        var role = message.Role.Value switch
        {
            "user" => AuthorRole.User,
            "assistant" => AuthorRole.Assistant,
            "system" => AuthorRole.System,
            _ => AuthorRole.Assistant
        };
        
        return new ChatMessageContent(role, message.Text ?? string.Empty);
    }
    
    public static Microsoft.Extensions.AI.ChatMessage ToExtensionsAI(
        this ChatMessageContent message)
    {
        var role = message.Role.Value switch
        {
            "user" => ChatRole.User,
            "assistant" => ChatRole.Assistant,
            "system" => ChatRole.System,
            _ => ChatRole.Assistant
        };
        
        return new ChatMessage(role, message.Content ?? string.Empty);
    }
}
```

#### 选项 2：统一使用 Semantic Kernel 类型
在工作流中全部使用 `Microsoft.SemanticKernel` 的类型，避免混用。

#### 选项 3：等待 API 统一
Microsoft 正在统一这些 API，可以等待后续版本更新。

## 优势对比

### 旧架构 (ConcurrentOrchestration)
- ❌ 使用预览版 API
- ❌ 生命周期管理复杂（需要 InProcessRuntime）
- ❌ 难以单元测试
- ❌ 扩展性差

### 新架构 (Agent Framework Workflows)
- ✅ 使用稳定的 Workflows API
- ✅ 清晰的 Executor 模式
- ✅ 每个 Executor 可独立测试
- ✅ 易于添加新分析师或步骤
- ✅ 内置进度事件
- ✅ 更好的错误处理

## 下一步行动

1. **实现类型适配器**：解决 API 兼容性问题
2. **完成编译**：确保所有代码编译通过
3. **运行测试**：验证工作流功能正常
4. **性能测试**：确保并发性能满足要求
5. **删除旧代码**：移除 `AnalystManager.cs`（旧实现）

## 测试计划

- [x] 创建测试文件 `MarketAnalysisWorkflowTest.cs`
- [ ] 测试基本分析流程
- [ ] 测试自定义提示词
- [ ] 测试进度事件触发
- [ ] 测试并发性能
- [ ] 测试错误处理

## 参考资料

- [Agent Framework Workflows 文档](https://learn.microsoft.com/zh-cn/agent-framework/tutorials/workflows/simple-concurrent-workflow)
- [Fan-Out/Fan-In 模式](https://learn.microsoft.com/zh-cn/agent-framework/tutorials/workflows/simple-concurrent-workflow#工作原理)
- [Executor 模式](https://learn.microsoft.com/zh-cn/agent-framework/concepts/executors)


