# StockSelectionWorkflow - AI选股工作流

## 概述

`StockSelectionWorkflow` 是基于 **Microsoft Agent Framework Workflows** 实现的AI选股工作流，采用确定性三步骤流程，消除了大模型调用的不确定性。

## 架构设计

### 工作流模式：顺序执行（Sequential Workflow）

```
用户请求 → 步骤1 → 步骤2 → 步骤3 → 结果
          ↓       ↓       ↓
    生成条件  执行筛选  AI分析
```

### 三个步骤（Executors）

#### 步骤1: GenerateCriteriaExecutor
- **输入**: `StockSelectionWorkflowRequest`（用户需求或新闻内容）
- **输出**: `string`（筛选条件 JSON）
- **实现**: 使用 YAML Prompt 将自然语言转换为结构化筛选条件
- **特点**: 纯 LLM 调用，无工具调用

#### 步骤2: ScreenStocksExecutor
- **输入**: `string`（筛选条件 JSON）
- **输出**: `ScreeningResult`（筛选结果 + 股票数据）
- **实现**: 直接调用 `StockScreenerPlugin.ScreenStocksAsync()`
- **特点**: **100%确定性，无AI参与**，纯代码执行

#### 步骤3: AnalyzeStocksExecutor
- **输入**: `ScreeningResult`（筛选结果）
- **输出**: `StockSelectionResult`（推荐报告）
- **实现**: 使用结构化输出（JSON）让AI分析股票数据
- **特点**: 纯分析，禁用工具调用（`FunctionChoiceBehavior.None()`）

## 核心优势

### 1. 确定性流程
- ❌ 旧方案: 让模型决定是否调用工具（`FunctionChoiceBehavior.Auto()`）
- ✅ 新方案: 编程控制每个步骤的执行，模型只负责分析

### 2. Token 优化
```
旧方案流程:
步骤1: 生成条件 (500 tokens)
步骤2: 模型推理 + 决定调用工具 + 调用 + 分析 (2000 tokens)
总计: 2500 tokens

新方案流程:
步骤1: 生成条件 (500 tokens)
步骤2: 直接调用插件 (0 tokens)
步骤3: 纯分析 (1500 tokens)
总计: 2000 tokens (节省 20%)
```

### 3. 可观测性
工作流提供事件流，实时监控执行状态：
- `ExecutorInvokedEvent` - 步骤开始
- `ExecutorCompletedEvent` - 步骤完成
- `WorkflowOutputEvent` - 最终结果
- `ExecutorErrorEvent` - 步骤失败

### 4. 可测试性
每个 Executor 可独立测试：
```csharp
// 测试步骤1
var executor1 = new GenerateCriteriaExecutor(...);
var result1 = await executor1.HandleAsync(request, context, cancellationToken);

// 测试步骤2
var executor2 = new ScreenStocksExecutor(...);
var result2 = await executor2.HandleAsync(result1, context, cancellationToken);

// 测试步骤3
var executor3 = new AnalyzeStocksExecutor(...);
var result3 = await executor3.HandleAsync(result2, context, cancellationToken);
```

## 使用方式

### 1. 用户需求分析
```csharp
var workflow = serviceProvider.GetRequiredService<StockSelectionWorkflow>();

var request = new StockRecommendationRequest
{
    UserRequirements = "寻找科技行业的价值股，市值超过50亿",
    RiskPreference = "moderate",
    MaxRecommendations = 10
};

var result = await workflow.AnalyzeUserRequirementAsync(request, cancellationToken);
```

### 2. 新闻热点分析
```csharp
var request = new NewsBasedSelectionRequest
{
    NewsContent = "新能源汽车人才缺口达百万...",
    MaxRecommendations = 5
};

var result = await workflow.AnalyzeNewsHotspotAsync(request, cancellationToken);
```

## 文件结构

```
src/Agents/Workflows/
├── StockSelectionWorkflow.cs              # 工作流主类
├── Executors/
│   ├── GenerateCriteriaExecutor.cs        # 步骤1: 生成筛选条件
│   ├── ScreenStocksExecutor.cs            # 步骤2: 执行股票筛选
│   └── AnalyzeStocksExecutor.cs           # 步骤3: AI分析结果
├── Models/
│   └── WorkflowModels.cs                  # 数据模型
└── README.md                              # 本文档
```

## 依赖注入配置

```csharp
// ServiceCollectionExtensions.cs
services.AddSingleton<StockSelectionWorkflow>();
services.AddSingleton<ILogger<GenerateCriteriaExecutor>>(...);
services.AddSingleton<ILogger<ScreenStocksExecutor>>(...);
services.AddSingleton<ILogger<AnalyzeStocksExecutor>>(...);
```

## 测试

参见 `tests/StockSelectionWorkflowTest.cs`：
- `TestStockSelectionWorkflow_AnalyzeUserRequirementAsync_WithValidRequest_ShouldReturnResult`
- `TestStockSelectionWorkflow_AnalyzeNewsHotspotAsync_WithValidRequest_ShouldReturnResult`
- `TestStockSelectionWorkflow_DeterministicExecution_ShouldFollowThreeSteps`

## 与 AnalystManager 的对比

| 特性 | StockSelectionWorkflow | AnalystManager |
|-----|----------------------|----------------|
| 场景 | 固定三步骤流程 | 多Agent并发协作 |
| 编排方式 | Agent Framework Workflows | SK Agents Orchestration |
| 确定性 | 100%（编程控制） | 部分（依赖模型） |
| Agent数量 | 无（纯Executor） | 4-6个分析师 |
| 并发 | 顺序执行 | 并发执行 |
| 适用场景 | 选股筛选 | 股票分析 |

## 迁移说明

### 从 StockSelectionManager 迁移

旧代码（SK 版本）：
```csharp
var manager = new StockSelectionManager(...);
var result = await manager.AnalyzeUserRequirementAsync(request);
```

新代码（Agent Framework Workflows 版本）：
```csharp
var workflow = new StockSelectionWorkflow(...);
var result = await workflow.AnalyzeUserRequirementAsync(request);
```

**接口完全兼容**，无需修改调用代码。

## 参考文档

- [Agent Framework - 简单顺序工作流](https://learn.microsoft.com/zh-cn/agent-framework/tutorials/workflows/simple-sequential-workflow)
- [Agent Framework - 结构化输出](https://learn.microsoft.com/zh-cn/agent-framework/tutorials/agents/structured-output)
- [Agent Framework - 函数工具](https://learn.microsoft.com/zh-cn/agent-framework/tutorials/agents/function-tools)

