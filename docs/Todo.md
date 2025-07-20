## TODO

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