# TODO

> 历史文档《P0-P1重构实施计划.md》与《升级重构核查报告.md》已归档删除，其未完成事项已合并至本清单。

## 高优先级

- **分析师产物存储生命周期管理**：`FileAnalystArtifactStore` 只有 Save/Get，每次分析在 `%APPDATA%/MarketAssistant/analyst-artifacts/{runId}/` 留一份产物，磁盘无限增长。需增加按 Run 清理策略（保留 N 天或归档联动删除）。
- **runId 注入方式加固**：当前 runId 依赖摘要文本让 LLM「抄写」到 `get_analyst_artifact` 工具参数，模型抄错即读不到产物；应通过工具上下文自动注入当前 runId。
- **E2E 验证降级路径**：聚合器「有界宽限（默认 30s）+ 降级发送」机制仅有确定性单测覆盖，需真实环境跑一次 E2E 确认降级报告可用，并按实际分析师耗时校准宽限期。
- **MAF Fan-In barrier 根因跟踪**：1.18 / 1.19 均未修复消息时序丢失缺陷，持续关注 release notes；修复后可移除聚合器宽限兜底与测试重试循环。

## Agent 与 Prompt

- `xml prompt` 方案评估与落地。
- 精细化任务指导：让主 Agent 输出更明确的目标、边界和结果格式。
- 将阶段性工作总结沉淀到外部记忆，并以轻量上下文继续后续任务。

## MCP 工具约束

- ~~空白名单默认不暴露任何工具；`AllowAllTools` 显式放行全部~~（已落地）。
- 待办：MCP 配置页列出服务器工具供逐个勾选（当前需手动编辑 `AllowedTools`）；旧配置迁移提示"请重新勾选 MCP 工具"。
- 评估按服务器或业务域拆分插件命名空间，例如 `mcp.<serverName>` 或 `mcp.search.<serverName>`。
- 继续完善函数调用范围限制与提示词级工具使用策略。

## RAG 与文档链路

- 继续收敛文档摄取链，避免 `PDF/DOCX -> Markdown -> 再解析 Markdown` 的双重解析路径持续扩散（DOCX 试点）。
- 摄取部分失败的 UI 三态汇总已实现，可补充端到端验收场景。
- SK → MAF 迁移收尾：RAG 向量存储（SqliteVec）、Web 搜索、文本分块仍依赖 SemanticKernel 包，待 MAF 提供稳定替代后迁移。

## 架构与交易

- **App.Services 继续拆分**：第一步已将模型接入基础设施拆出为 `src/MarketAssistant.Infrastructure`；下一步可迁移 `Infrastructure/Factories` 中剩余的工厂类与 AdaptiveCards 解析层。
- **测试基线治理收尾**：分类历史遗留的集成/E2E 失败用例；确认 CI 在 PR 上自动触发（当前仅手动 workflow_dispatch）。

## 已完成（近期落地项）

- MAF 1.13 → 1.17 → **1.19.0** 升级（无破坏性影响）。
- ~~聚合器有界宽限 + 降级发送、产物落盘全异步化~~（P0-1 缓解收尾）。
- ~~分析师产物引用传递 + 协调器只读工具~~（P1-07）。
- ~~检索距离方向~~、~~摄取结果三态与维度校验~~、~~A 股 HTTP 下沉 DataProviders~~、~~MCP 空白名单默认不暴露~~ 等 P0/P1 正确性重构项。
- ~~IExchangeClientFactory 抽象落地，组合根移除手动构建 Binance 对象~~（P1-5）。
- ~~测试标签治理：非标 Agent 标签改 Integration、9 个无标签测试补 Unit/Integration——Unit 过滤器现覆盖全部确定性测试~~（P1-7）。
- ~~CI 默认只跑 Unit（unit.runsettings + TestCaseFilter）~~。
- ~~RAG 摄取失败三态透出 UI~~（复核确认已有实现）。

## 多 Agent 协作

- ~~子 Agent 结果写入文件系统并传回轻量引用~~（已通过 `IAnalystArtifactStore` + 摘要传递 + `get_analyst_artifact` 工具落地；生命周期管理与 runId 注入两项收尾见高优先级清单）。
