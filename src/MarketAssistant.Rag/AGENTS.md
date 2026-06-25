# MarketAssistant.Rag — AGENTS.md

独立的 RAG 基础能力项目，负责文档解析、文本清洗、分块、嵌入、向量检索、查询改写与重排；**不承载 UI、Agent 工作流、业务编排或市场服务**。

---

## 目录结构

```
MarketAssistant.Rag/
├── Extensions/
│   └── ServiceCollectionExtensions.cs   ← AddRagServices() 注册入口
├── Infrastructure/
│   └── Factories/
│       ├── IEmbeddingFactory.cs         ← 文本嵌入生成器工厂契约
│       └── IWebTextSearchFactory.cs     ← Web 搜索工厂契约
├── Interfaces/
│   ├── IRagIngestionService.cs          ← 文档摄取接口
│   ├── IRetrievalOrchestrator.cs        ← 检索编排接口
│   ├── IRerankerService.cs              ← 重排接口
│   ├── IQueryRewriteService.cs          ← 查询改写接口
│   ├── ITextCleaningService.cs          ← 文本清洗接口
│   ├── ITextChunkingService.cs          ← 文本分块接口
│   ├── IDocumentBlockReader.cs          ← 文档块读取接口
│   ├── IMarkdownConverter.cs            ← Markdown 转换接口
│   ├── IImageEmbeddingService.cs        ← 图像嵌入接口
│   └── IImageStorageService.cs          ← 图像存储接口
├── Services/
│   ├── RagIngestionService.cs           ← 文档清洗/分块/向量化/写入
│   ├── RetrievalOrchestrator.cs         ← 查询改写 + 检索 + 去重 + 重排
│   ├── RerankerService.cs               ← 启发式重排
│   ├── QueryRewriteService.cs           ← 查询重写
│   ├── TextCleaningService.cs           ← 文本清洗
│   ├── TextChunkingService.cs           ← 文本分块
│   ├── DocxMarkdownConverter.cs         ← DOCX → Markdown
│   ├── PdfMarkdownConverter.cs          ← PDF → Markdown
│   ├── DocxBlockReader.cs               ← DOCX 文档块读取
│   ├── PdfBlockReader.cs                ← PDF 文档块读取
│   ├── MarkdownDocumentBlockReader.cs   ← Markdown 文档块读取
│   ├── DocumentBlockReaderFactory.cs    ← 文档读取器工厂
│   ├── MarkdownConverterFactory.cs      ← 转换器工厂
│   ├── ClipImageEmbeddingService.cs     ← CLIP 图像嵌入与 caption 降级策略
│   └── LocalImageStorageService.cs      ← 本地图像存储
├── TextParagraph.cs                     ← 向量检索的段落实体
└── GlobalUsing.cs
```

---

## 边界约定

- 本项目只放 **RAG 基础能力**：文档解析、清洗、分块、嵌入、存储映射、检索、重排。
- Agent、Workflow、Tool 实现放在 `MarketAssistant.App.Services` 或 `MarketAssistant.Agents`，不要回流到这里。
- Avalonia 视图、导航、通知、页面状态属于 `MarketAssistant.App`。
- 市场业务服务、首页/收藏/分析等应用服务属于 `MarketAssistant.App.Services`。
- 仅保留与 RAG 能力直接相关的接口和服务；不要在这里堆放通用业务逻辑。

---

## 编码约定

- 优先复用现有接口：新增摄取、检索或转换能力时，先检查 `Interfaces/` 和现有工厂是否已覆盖扩展点。
- 文档解析流程优先走统一抽象：`IMarkdownConverter`、`IDocumentBlockReader`、对应工厂；不要在上层服务里按文件类型手写分支。
- 检索流程保持分层：查询改写、召回、去重、重排分别在各自服务中完成，避免在单个服务中混合实现。
- 向量或模型调用失败时优先保留降级路径，避免因单个模型或外部依赖故障导致整条链路不可用。
- 只有与复杂算法、模型 I/O 或降级策略直接相关的代码才添加简短注释。

---

## DI 注册

- 注册入口：`Extensions/ServiceCollectionExtensions.cs` 中的 `AddRagServices()`。
- 若新增 RAG 服务，优先在该扩展方法统一注册，不要把注册逻辑散落到 UI 或业务层。
- 本项目依赖的 `VectorStore`、`IEmbeddingFactory` 等宿主级依赖，应由上层项目提供。

---

## 模型与文件处理

- `ClipImageEmbeddingService` 支持 CLIP ONNX 模型，默认从 `models/clip-image.onnx` 加载，也可通过环境变量 `CLIP_IMAGE_ONNX` 指定路径。
- PDF/DOCX 解析依赖 `PdfPig`、`DocumentFormat.OpenXml`；修改解析逻辑时优先保证结构化输出稳定，不要为单个样例过拟合。
- 重排服务当前为启发式实现，若引入模型化重排，需要明确成本、延迟与降级策略。

---

## 构建

```bash
dotnet build src/MarketAssistant.Rag/MarketAssistant.Rag.csproj -c Debug
```