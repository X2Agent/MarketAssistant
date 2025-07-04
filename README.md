## ✨ 简介

本项目基于.NET MAUI开发，结合AI大模型构建的股票分析工具。目前已支持A股，未来计划加入港股、美股、虚拟币等支持。

支持市场整体/个股情绪分析，K线技术指标分析等功能。本项目仅供学习研究，投资有风险，入市需谨慎。

## TODO

- 增加股票推荐功能
- 选定目录文档(Pdf,Docx)向量化搜索
- SK Web Search 支持
- MCP Client 支持
- https://github.com/microsoft/semantic-kernel/blob/main/docs/decisions/0070-declarative-agent-schema.md
- https://github.com/microsoft/semantic-kernel/blob/main/docs/decisions/0072-agents-with-memory.md
- https://github.com/microsoft/semantic-kernel/blob/main/docs/decisions/0072-context-based-function-selection.md
- https://learn.microsoft.com/zh-cn/semantic-kernel/frameworks/agent/agent-contextual-function-selection?pivots=programming-language-csharp#how-contextual-function-selection-works


- 与其让子Agent把大块的结构化结果（代码、报告）通过对话历史传给主Agent（这会失真且昂贵），不如让它直接调用工具将产出物存到文件系统，再把轻量的“引用/指针”传回去。这能最大化保证并降低Token成本。
- 总结已完成的阶段性工作，存入外部记忆，然后带着干净的上下文继续，通过“记忆”来保持连续性
- 精细化任务指导（目前还是硬编码规划），主Agent会给子Agent非常明确的目标、输出格式和任务边界

## 📊 主要功能

### 股票分析

- 基本面分析：公司基本情况、财务状况、行业地位等
- 技术面分析：K线图、技术指标、趋势分析
- 新闻事件分析：相关新闻、公告解读
- 市场情绪分析：市场整体情绪、个股情绪
- 财务分析：季度财报、现金流、盈利能力

### 文档向量化

- 支持PDF、DOCX等文档格式
- 向量化搜索，获取最新相关信息
- 结合LLM进行智能分析

### 数据可视化

- 股票K线图展示
- 技术指标可视化
- 分析结果直观展示

## 🖥️ 平台支持

- Windows (WinUI)
- MacOS

## 🛠️ 技术栈

- .NET MAUI
- Semantic Kernel

## 📸 功能截图

*即将添加*

## 📊 Star History

[![Star History Chart](https://api.star-history.com/svg?repos=X2Agent/MarketAssistant&type=Date)](https://www.star-history.com/#X2Agent/MarketAssistant&Date)

## 🙏 鸣谢

本项目大部分代码由 AI 智能编程助手生成。在此特别感谢 Trae AI 强大的代码生成能力，为本项目的开发提供了极大帮助。


## 📄 许可证

Apache License 2.0