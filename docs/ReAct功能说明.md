# AI 对话与工具调用架构说明

## 概述

MarketAssistant 的 AI 交互基于 **Microsoft Agent Framework (MAF)** 的 `ChatClientAgent` 实现。  
工具调用通过 LLM 原生的 **Function Calling** 机制自动完成，MAF 框架负责管理工具调用循环——无需手动解析 `Thought/Action/Observation` 文本标签。

## 架构组件

### MarketChatSession

对话会话管理器，职责：

- 封装 `ChatClientAgent`，管理会话生命周期
- 通过 `InjectAnalysisContext` 将 Workflow 阶段的分析结果注入系统指令
- 合并 **GroundingSearchTools**（知识库 + 网络搜索）和 **MCP 工具** 作为可用工具集
- 自维护对话历史镜像，支持通过 `GetConversationHistoryAsync` 获取

### 工具调用机制

MAF 的 `ChatClientAgent` 使用 LLM 的原生 Function Calling API：

1. 用户发送消息
2. LLM 判断是否需要调用工具，生成结构化的工具调用请求
3. MAF 框架自动执行工具并将结果返回给 LLM
4. LLM 基于工具结果继续推理或生成最终回复
5. 如需多次工具调用，框架自动循环步骤 2-4

与旧版手动 ReAct 文本解析的区别：

| 旧版（已移除） | 当前版本 |
|---|---|
| 手动解析 `Thought:`/`Action:` 文本 | LLM 原生 Function Calling |
| 自定义循环 `ProcessWithReActAsync` | MAF 框架自动管理 |
| 文本格式 `Final Answer:` 检测 | LLM 直接返回自然语言回复 |
| 手动迭代计数和超时 | 框架内置管理 |

### 可用工具

#### GroundingSearchTools

综合信息检索工具，根据用户设置自动选择搜索策略：

- **知识库搜索**：检索本地 RAG 向量库中的文档
- **网络搜索**：通过 Bing/Brave/Tavily 搜索实时信息
- **混合搜索**：同时执行两者并合并去重

#### MCP 工具

通过 Model Context Protocol 接入的外部工具，由用户在设置页配置。

### 分析上下文注入

`MarketAnalysisWorkflow` 产出的多维分析报告通过以下流程注入对话：

1. `AgentAnalysisViewModel` 调用 `ChatSidebarViewModel.InitializeWithAnalysisHistory`
2. `ChatSidebarViewModel` 调用 `MarketChatSession.InjectAnalysisContext`
3. 分析结果被整合到系统指令的 `<analysis_context>` 段中
4. 后续对话中，LLM 基于该上下文回答追问

## 提示词策略

系统指令（`BuildAgentInstructions`）包含以下结构化段落：

- `<role>` — 角色定义与当前关注标的
- `<analysis_context>` — 注入的分析师报告（如有）
- `<instructions>` — 行为指引：优先引用分析上下文，必要时使用搜索工具补充
- `<quality_standards>` — 数值精度、来源引用要求
- `<forbidden>` — 禁止客套话和问句结尾

## 相关文件

- `src/Agents/MarketChatSession.cs` — 对话会话核心实现
- `src/Agents/Tools/GroundingSearchTools.cs` — 综合搜索工具
- `src/Services/Mcp/McpService.cs` — MCP 工具管理
- `src/ViewModels/ChatSidebarViewModel.cs` — 对话 UI 视图模型
- `src/ViewModels/AgentAnalysisViewModel.cs` — 分析上下文注入调用方

## 参考资料

- [Microsoft Agent Framework](https://github.com/microsoft/agents) — MAF 框架
- [Microsoft.Extensions.AI](https://learn.microsoft.com/en-us/dotnet/ai/ai-extensions) — AI 扩展库
- [ReAct: Synergizing Reasoning and Acting in Language Models](https://arxiv.org/abs/2210.03629) — 理论基础
