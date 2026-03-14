# MarketAssistant.App.Services — AGENTS.md

桌面应用的运行时与业务编排层。负责 Agent 实现、Workflow、Tool 实现、RAG、MCP、交易编排、业务服务与运行时基础设施；**不承载 Avalonia 视图、导航控件或页面样式**。

---

## 目录结构

```
MarketAssistant.App.Services/
├── Agents/                        ← Agent 实现、工作流、执行器、工具实现
├── Applications/                  ← 面向 UI 的业务服务（Assets/Home/Favorites/Analysis 等）
├── Infrastructure/                ← 工厂、配置、通用运行时适配
├── Rag/                           ← 文档解析、向量化、检索与重排
├── Services/                      ← 横切服务（Archive/Browser/Cache/Market/Mcp/Settings 等）
└── Trading/                       ← 交易引擎、风控、持久化与监控
```

---

## 边界约定

- 本项目承载所有非 UI 运行时代码；新增 Agent、Tool、Workflow、RAG、MCP、交易服务时，优先放在这里。
- Avalonia 视图、窗口、导航、通知、对话框实现属于 `MarketAssistant.App`，不要回流到本项目。
- 市场特定能力通过 `MarketType` Keyed Services 注册，不在业务逻辑中堆叠 `if/else` 区分市场。
- Tool 接口定义保留在 `MarketAssistant.Agents/Tools/Abstractions/`，本项目只放实现。
- 对外部 HTTP/API 的直接调用优先收敛到 `MarketAssistant.DataProviders` 或已存在的统一服务中，不在多个 Tool/Service 内重复实现。

---

## DI 注册

- 业务注册根：`Services/ServiceCollectionExtensions.cs` 中的 `AddBusinessServices()`。
- UI 宿主通过 `MarketAssistant.App/Services/ServiceCollectionExtensions.cs` 调用本项目注册入口。
- 新增服务时，按职责放到对应扩展方法附近，避免把所有注册逻辑散落到 ViewModel 或窗口构造中。

---

## MAF 约定

- 分析师实现放在 `Agents/Analysts/`，统一通过 `IAnalystAgentFactory` 创建。
- 工作流实现放在 `Agents/*Workflow*/`，执行器放在 `Executors/`。
- 通用知识型 Skill 资源保留在 `MarketAssistant.App/skills/` 作为内容文件输出，本项目通过 `FileAgentSkillsProvider` 加载，不在这里重复存放一份。

---

## 构建

```bash
dotnet build src/MarketAssistant.App.Services/MarketAssistant.App.Services.csproj -c Debug
```