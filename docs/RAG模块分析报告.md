# RAG 模块分析与重构实施报告

> 核验日期：2026-07-30  
> 分析对象：`src/MarketAssistant.Rag/` 及其在 `MarketAssistant.App`、`MarketAssistant.App.Services`、`tests/Vectors` 中的调用与测试  
> 适用读者：负责实施重构的初级、中级开发者，以及负责方案评审的高级开发者  
> 文档状态：已按当前源码核验，可作为重构实施基线

---

## 1. 结论先行

当前 RAG 模块已经具备完整的基础链路：文档解析、文本清洗、语义分块、文本向量化、SQLiteVec 持久化、查询改写、向量召回和启发式重排。

但是，当前实现存在两个必须先修复的正确性问题：

1. `TextParagraph.TextEmbedding` 使用 `CosineDistance`，检索结果应按“距离越小越相关”解释；当前去重和重排却按“分数越大越相关”处理，可能造成排序反向。
2. 文本向量维度固定为 1024，但摄取链没有显式校验；块级异常被捕获后不向上报告，界面仍可能把部分失败的文件计为成功。

在这两个问题修复前，不应优先投入混合检索、LLM Query Rewrite 或解析器大改。否则新增能力会建立在不可靠的排序和摄取结果之上，难以判断改动是否真正提升质量。

推荐实施顺序：

```text
P0 检索分数方向
  -> P0 向量维度与摄取结果
  -> P1 文档替换/删除与键稳定性
  -> P1 保留检索元数据并扩展上下文
  -> P1 建立最小离线评估基线
  -> P1 收敛伪多模态设计
  -> P2 收敛 PDF/DOCX 解析链
  -> P2 配置化查询词表
  -> P2 基于评估结果决定是否引入混合检索
```

---

## 2. 核验范围与验证结果

### 2.1 核验依据

本报告已对照以下内容：

- `src/MarketAssistant.Rag/` 的模型、接口、服务和依赖注入注册；
- `src/MarketAssistant.App/ViewModels/SettingsPageViewModel.cs` 的摄取调用；
- `src/MarketAssistant.App.Services/Agents/Tools/GroundingSearchTools.cs` 的检索调用；
- `Directory.Packages.props` 中的实际 NuGet 版本；
- `tests/Vectors/` 下的单元测试和集成测试；
- `docs/Todo.md` 中已有的 RAG 待办。

本文中的行号基于 2026-07-30 的源码快照。后续代码变化时，以文件路径、类型名和方法名为主要定位依据。

### 2.2 当前验证结果

| 验证项 | 结果 | 说明 |
|---|---:|---|
| 解决方案构建 | 通过 | 0 个错误，19 个警告 |
| RAG 核心单元测试 | 49/49 通过 | 覆盖 cleaning、chunking、mapper、rewrite、reranker、Markdown reader、CLIP 基础行为 |
| `tests/Vectors` 全套测试 | 69/72 通过 | 3 个集成测试因未配置 `JINA_API_KEY` 失败，不属于已确认的代码回归 |

当前测试数量不能证明检索质量良好。现有测试主要验证功能路径，尚未验证：

- `CosineDistance` 分数方向；
- 非 1024 维文本模型；
- 部分块失败时的文件级状态；
- 重复摄取后的旧记录清理；
- 邻接上下文扩展；
- 真实 CLIP ONNX 推理和真正的跨模态检索；
- Recall@K、MRR、nDCG 等离线质量指标。

---

## 3. 当前架构

### 3.1 摄取链路

```text
File
  -> DocumentBlockReaderFactory
  -> IDocumentBlockReader
  -> DocumentBlock[]
  -> DocumentBlockMapper
  -> TextParagraph[]
  -> IEmbeddingGenerator<string, Embedding<float>>
  -> VectorStoreCollection.UpsertAsync
  -> SQLiteVec
```

入口：`RagIngestionService.IngestFileAsync`。

PDF/DOCX 当前实际路径为：

```text
PDF/DOCX
  -> PdfMarkdownConverter / DocxMarkdownConverter
  -> Markdown 文本
  -> Markdig 再解析
  -> DocumentBlock[]
```

证据：

- `Services/PdfBlockReader.cs:21-26`
- `Services/DocxBlockReader.cs:20-23`
- `Services/MarkdownDocumentBlockReader.cs:37-65`

### 3.2 检索链路

```text
User Query
  -> QueryRewriteService 生成最多 3 个词表变体
  -> 原查询 + 变体批量生成文本向量
  -> 每个查询搜索 TextEmbedding
  -> 合并候选
  -> 按 Link + Name + Value 去重
  -> RerankerService 启发式重排
  -> Top-K TextSearchResult
```

入口：`RetrievalOrchestrator.RetrieveAsync`。

当前只搜索文本向量：

```csharp
var vectorSearchOptions = new VectorSearchOptions<TextParagraph>
{
    VectorProperty = r => r.TextEmbedding
};
```

证据：`Services/RetrievalOrchestrator.cs:78-82`。

### 3.3 关键类型职责

| 类型 | 当前职责 |
|---|---|
| `TextParagraph` | SQLiteVec 记录模型，保存文本、两个向量字段及文档元数据 |
| `RagConstants` | 固定向量维度为 1024 |
| `RagIngestionService` | 文档级摄取编排 |
| `DocumentBlockMapper` | 将不同块类型转换为 `TextParagraph` |
| `TextCleaningService` | 文本清洗 |
| `TextChunkingService` | 400 token 分块，40 token overlap |
| `QueryRewriteService` | 基于硬编码词表生成查询变体，不调用 LLM |
| `RetrievalOrchestrator` | 批量生成查询向量、多查询召回、去重和重排 |
| `RerankerService` | 向量、关键词、时效、长度和多样性启发式评分 |
| `ClipImageEmbeddingService` | CLIP ONNX 图像向量；失败时返回哈希向量 |

---

## 4. 已确认的合理设计

以下设计可以保留，不建议在第一阶段重写：

1. **RAG 独立类库**  
   模块通过接口被上层调用，分层方向符合项目约束。

2. **检索阶段不强制依赖 LLM**  
   Query Rewrite 和 Reranker 目前均为本地启发式逻辑，延迟和费用可控。后续即使增加 LLM 改写，也应保持可关闭和可降级。

3. **查询向量批量生成**  
   `RetrievalOrchestrator.cs:66-71` 一次生成原查询及变体的向量，减少 HTTP 往返。

4. **摄取时按块批量生成文本向量**  
   `RagIngestionService.cs:113-125` 对一个 `DocumentBlock` 产生的多个段落批量生成向量。注意：这是“每块一次”，不是“整篇文档一次”。

5. **结构化元数据模型**  
   `Order`、`Section`、`BlockKind`、`ContentHash`、`PublishedAt` 已存在，为上下文扩展和增量更新提供了基础。

6. **金融领域的 CJK n-gram 和关键词权重**  
   作为轻量级相关性信号具有实用价值，但权重需要评估集校准，不能仅凭主观调整。

7. **图片按 SHA-256 做文档内精确去重**  
   该逻辑适合识别字节完全相同的图片，但不具备感知相似去重能力。

---

## 5. 已确认问题与优先级

| ID | 优先级 | 问题 | 当前事实 | 主要影响 |
|---|---|---|---|---|
| RAG-001 | P0 | 余弦距离方向处理反了 | 配置为 `CosineDistance`，代码却保留最高分并按降序重排 | 最相关结果可能被降权 |
| RAG-002 | P0 | 文本向量维度无显式校验 | Schema 固定 1024，文本向量原样写入 | 写入/检索失败，问题定位困难 |
| RAG-003 | P0 | 块失败被吞掉，文件仍可能显示成功 | 块级 catch 只记 Warning；UI 随后 `successCount++` | 知识库处于不完整状态但用户不知情 |
| RAG-004 | P1 | 重复摄取不会删除旧块 | 只有 `UpsertAsync`，没有文档级替换/删除 | 文档更新后新旧内容同时被召回 |
| RAG-005 | P1 | 部分记录键可能覆盖合法块 | 标题、列表、表格、图片键未包含稳定顺序，同内容重复出现时可能相同 | 同一文档内记录被覆盖 |
| RAG-006 | P1 | 检索过早丢弃元数据 | 搜索后立即转换为 `TextSearchResult` | 无法可靠使用 `Order`、`Section`、`PublishedAt` |
| RAG-007 | P1 | 没有邻接上下文 | 只返回命中的独立段落 | 表格、标题后的正文缺少语境 |
| RAG-008 | P1 | “多模态检索”描述与实现不符 | 只搜索 `TextEmbedding`，`ImageEmbedding` 未进入查询链路 | 维护者可能基于错误假设继续扩展 |
| RAG-009 | P1 | 哈希向量被当作图像语义降级 | CLIP 失败后返回 SHA-256 派生向量 | 向量确定但无语义，可能污染未来图像检索 |
| RAG-010 | P2 | PDF/DOCX 双重解析 | 先转 Markdown，再由 Markdig 解析 | 结构信息丢失、启发式叠加 |
| RAG-011 | P2 | Query Rewrite 和评分词表硬编码 | 词表、停用词、权重均在代码内 | 维护成本高，调整必须发版 |
| RAG-012 | P2 | 只有向量召回 | 当前 SQLiteVec connector 不提供可直接使用的 Hybrid Search | 股票代码、数值、专有名词召回不足 |
| RAG-013 | P1 | 缺少离线质量评估 | 没有标准问题集和排序指标 | 无法证明重构提升或退化 |

---

## 6. 关键问题详解

### 6.1 RAG-001：余弦距离方向错误

模型配置：

```csharp
[VectorStoreVector(
    RagConstants.EmbeddingDimension,
    DistanceFunction = DistanceFunction.CosineDistance,
    IndexKind = IndexKind.Hnsw)]
public Embedding<float> TextEmbedding { get; set; } = default!;
```

位置：`TextParagraph.cs:28-30`。

在当前 `Microsoft.SemanticKernel.Connectors.SqliteVec 1.74.0-preview` 和 `CosineDistance` 配置下，应按距离语义处理：距离越小越相关。当前代码存在三处反向处理：

- `RetrievalOrchestrator.cs:127-131`：去重时 `OrderByDescending`，保留最大值；
- `RerankerService.cs:90-105`：最大值被归一化为最高相关度；
- `RerankerService.cs:122-125`：异常回退按降序返回。

该问题必须由集成测试锁定。未来升级向量库连接器时，也必须重新验证 `VectorSearchResult.Score` 的具体语义，不能只根据属性名猜测。

### 6.2 RAG-002：文本向量不会被本模块静默截断

原实现中，截断/补零只发生在 CLIP 图像向量：

- `ClipImageEmbeddingService.cs:419-440`

文本向量在以下位置被原样写入：

- `RagIngestionService.cs:118-124`

因此，非 1024 维文本模型的真实风险不是“被本模块静默截断”，而是与 1024 维 Schema 不兼容，最终在写入或搜索时失败。

第一阶段不建议实现“动态向量维度”。`TextParagraph` 的向量维度通过特性固定，动态支持多个维度还涉及 Collection Schema、存量库迁移和模型切换策略。正确的短期方案是：

- 明确系统当前只支持 1024 维文本 Embedding；
- 首次生成向量后立即校验维度；
- 不匹配时失败退出并给出可操作错误；
- 更换模型或维度时要求重建对应 Collection。

### 6.3 RAG-003：部分失败被报告为成功

块级异常在 `RagIngestionService.cs:128-132` 被捕获后仅记录警告，方法继续执行并最终正常返回。

调用方 `SettingsPageViewModel.cs:523-526` 只要方法没有抛出异常，就会增加成功计数。

结果是：文档可能只有部分块进入向量库，但界面显示“成功向量化”。这是数据完整性问题，不只是日志体验问题。

### 6.4 RAG-004：增量摄取不是“只插入新 ContentHash”

简单地跳过已存在的 `ContentHash` 不能解决文档更新：

- 被删除的旧段落仍会保留；
- 段落顺序变化后，旧的邻接关系仍可能存在；
- 摄取中途失败会留下新旧混合数据。

目标语义应定义为“按文档替换”：

```text
解析并生成新版本
  -> 校验全部结果
  -> 写入新记录
  -> 删除该文档不再存在的旧记录
  -> 更新文档清单
```

在没有事务能力时，至少需要文档清单和失败恢复策略，不能只比较单个块哈希。

### 6.5 RAG-005：同内容块可能产生相同 Key

例如标题键：

```csharp
Key = $"{fileHash}:hdg:{headingBlock.Level}:{hash[..8]}"
```

位置：`DocumentBlockMapper.cs:81-100`。

同一文件中若两次出现同级、同文本标题，Key 相同，后写入记录会覆盖先写入记录。列表、表格和图片键也存在相同风险。

键应同时包含稳定文档标识、块类型、顺序和内容哈希，例如：

```text
{documentId}:{blockKind}:{order:D6}:{contentHashPrefix}
```

不要使用随机 GUID，否则无法稳定比较新旧版本。

### 6.6 RAG-006/RAG-007：元数据被过早丢弃

`RetrievalOrchestrator.cs:103-112` 将 `TextParagraph` 立即压缩成只有 `Name`、`Link`、`Value` 的 `TextSearchResult`。

这会丢失：

- `Order`：无法定位前后块；
- `Section`：无法补充章节标题；
- `PublishedAt`：Reranker 只能从 URL/正文猜时间；
- `BlockKind`：无法针对表格、标题、图片使用不同策略；
- `ContentHash`：无法做候选级精确去重。

正确做法是内部全程保留 `TextParagraph`，只在公开 API 边界转换为 `TextSearchResult`。

### 6.7 RAG-008/RAG-009：当前不是跨模态检索

当前事实：

- 图片块会生成 Caption；
- Caption 会继续生成 `TextEmbedding`，因此可以通过文本查询召回图片说明；
- 图片还会生成 `ImageEmbedding`；
- 检索只指定 `TextEmbedding`，没有查询或融合 `ImageEmbedding`。

所以当前具备的是“图片 Caption 的文本检索”，不是“文本与图片共享语义空间的跨模态检索”。

此外，仅把不同模型的输出都调整为 1024 维，并不能让它们进入同一语义空间。CLIP 失败后生成的哈希向量只具有确定性，不具有语义相关性，不应参与检索排序。

### 6.8 RAG-012：不能直接利用 `IsFullTextIndexed` 完成混合检索

虽然 `TextParagraph.Text` 标记了：

```csharp
[VectorStoreData(IsFullTextIndexed = true)]
```

但项目当前使用 `Microsoft.SemanticKernel.Connectors.SqliteVec 1.74.0-preview`，该连接器不能据此提供现成的全文检索或 Hybrid Search API。

因此，不应让开发者直接在 `RetrievalOrchestrator` 中调用一个并不存在的 FTS 搜索能力。混合检索需要先做技术选型：

1. 更换为明确支持关键词检索与向量检索的存储实现；或
2. 引入独立、成熟的词法检索组件，再使用 RRF 融合。

本项目坚持避免手写基础设施，不建议在业务服务中直接拼接 FTS5 SQL。

---

## 7. 目标架构与边界

### 7.1 目标摄取链

```text
File
  -> IDocumentBlockReader
  -> DocumentBlock[]
  -> DocumentBlockMapper
  -> TextParagraph[]
  -> 文本向量批量生成
  -> 维度与数量校验
  -> 文档级替换写入
  -> RagIngestionResult
```

核心约束：

- 一个文件的摄取结果必须明确区分成功、部分失败和失败；
- 不允许块失败后静默计为文件成功；
- 同一文档重复摄取后，旧版本记录必须可清理；
- 取消令牌必须传播到解析、Embedding 和存储调用；
- 1024 维约束在模型调用后立即验证。

### 7.2 目标检索链

```text
User Query
  -> 可配置 Query Rewrite
  -> 批量文本 Embedding
  -> 向量召回（保留 TextParagraph + Distance）
  -> 候选去重
  -> 距离方向正确的 Reranker
  -> Section/Neighbor Context 扩展
  -> 最终转换为 TextSearchResult
```

后续可选扩展：

```text
向量召回 ----┐
             ├-> RRF 融合 -> Reranker -> Context Expansion
关键词召回 --┘
```

### 7.3 本轮重构非目标

以下内容不要与 P0 修复放在同一个 PR：

- 引入 LLM Query Rewrite；
- 同时支持任意 Embedding 维度；
- 更换向量数据库；
- 一次性重写 PDF、DOCX、Markdown 三套解析器；
- 实现文本查询到 CLIP 图片向量的跨模态搜索；
- 调整所有 Reranker 权重。

原因：这些改动会扩大验证面，使正确性修复难以单独评审和回归。

---

## 8. 分阶段重构实施手册

### 阶段 0：建立基线

难度：初级开发者可执行。  
目标：在修改前固定当前行为和验证命令。

#### 操作步骤

1. 运行解决方案构建：

```bash
dotnet build MarketAssistant.slnx -c Debug
```

2. 运行 RAG 单元测试：

```bash
dotnet test tests/TestMarketAssistant.csproj -c Debug --filter "FullyQualifiedName~TestMarketAssistant.Vectors"
```

3. 如果没有配置 `JINA_API_KEY`，记录相关集成测试为环境阻塞，不要为了“全绿”删除或弱化断言。
4. 保存至少一个中文 Markdown、一个包含表格的 DOCX、一个 PDF 作为回归样本。测试样本不得包含密钥或私人数据。

#### 完成标准

- 构建结果已记录；
- 失败测试已区分代码失败和环境失败；
- 后续每个阶段均可重复执行同一组命令。

---

### 阶段 1：修复距离分数方向（RAG-001）

难度：中级开发者主导，初级开发者补测试。  
目标：统一使用“距离越小越相关”的语义，禁止继续使用含糊的 `VectorScore` 命名。

#### 修改文件

- `src/MarketAssistant.Rag/Interfaces/IRerankerService.cs`
- `src/MarketAssistant.Rag/Services/RetrievalOrchestrator.cs`
- `src/MarketAssistant.Rag/Services/RerankerService.cs`
- `tests/Vectors/RerankerServiceTest.cs`
- `tests/Vectors/RetrievalOrchestratorIntegrationTest.cs`

#### 操作步骤

1. 将 `ScoredSearchResult.VectorScore` 重命名为 `VectorDistance`。
2. 去重时保留距离最小的结果：

```csharp
.Select(group => group.OrderBy(item => item.VectorDistance).First())
```

3. Reranker 的归一化改为距离反转：

```csharp
var normalizedSimilarity = range < 1e-9
    ? 1.0
    : (maxDistance - item.VectorDistance) / range;
```

4. 异常回退改为按距离升序。
5. 日志字段使用 `distance` 和 `similarity`，不要继续统称 `score`。
6. 增加单元测试，至少覆盖：
   - 距离 0.1 的结果必须排在 0.8 前；
   - 同一个结果被多个查询召回时保留最小距离；
   - 所有距离相同时行为稳定；
   - Reranker 抛出异常时仍按距离升序。
7. 增加 SQLiteVec 集成测试，用两个可控向量验证真实连接器返回顺序和距离含义。

#### 禁止做法

- 不要只把最终 `OrderByDescending` 改成 `OrderBy`，而保留错误的 min-max 归一化；
- 不要使用 `1 - distance` 后直接假设结果一定在 `[0,1]`；
- 不要同时调整 0.6/0.2/0.1/0.1 权重。

#### 完成标准

- 所有新增测试通过；
- 已有 Reranker 测试通过；
- 代码中不再出现把 `CosineDistance` 当作“越大越好”的路径；
- `VectorScore` 命名已从该链路移除。

---

### 阶段 2：修复维度校验和摄取结果（RAG-002/RAG-003）

难度：中级开发者主导。  
目标：任何块失败都必须进入结构化结果，向量维度错误必须在 Upsert 前被明确拒绝。

#### 修改文件

- `src/MarketAssistant.Rag/Interfaces/IRagIngestionService.cs`
- `src/MarketAssistant.Rag/Services/RagIngestionService.cs`
- `src/MarketAssistant.App/ViewModels/SettingsPageViewModel.cs`
- `tests/Vectors/RagIngestionServiceIntegrationTest.cs`
- 新增对应的摄取结果单元测试文件

#### 建议类型

```csharp
public sealed record RagIngestionFailure(
    int BlockOrder,
    string ErrorCode,
    string Message);

public sealed record RagIngestionResult(
    int BlockCount,
    int ParagraphCount,
    IReadOnlyList<RagIngestionFailure> Failures)
{
    public bool IsSuccess => Failures.Count == 0;
    public bool IsPartialSuccess => ParagraphCount > 0 && Failures.Count > 0;
}
```

接口建议增加取消令牌并返回结果：

```csharp
Task<RagIngestionResult> IngestFileAsync(
    VectorStoreCollection<string, TextParagraph> collection,
    string filePath,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    CancellationToken cancellationToken = default);
```

#### 操作步骤

1. 为每个块记录成功段落数和失败原因。
2. 生成向量后先校验：
   - `embeddings.Count == paragraphList.Count`；
   - 每个 `embeddings[i].Vector.Length == RagConstants.EmbeddingDimension`。
3. 校验失败时不要进入 `UpsertAsync`。
4. `OperationCanceledException` 必须继续抛出，不得转换成普通失败。
5. 其余块异常可以继续处理后续块，但必须加入 `Failures`。
6. UI 根据结果分别统计：
   - 成功；
   - 部分成功；
   - 失败。
7. 完成提示中显示失败文件和失败块数量，不得把部分成功计入完全成功。
8. 所有异步调用传递 `CancellationToken`。

#### 错误消息要求

维度错误至少包含：

```text
Embedding dimension mismatch. Expected 1024, actual 1536.
Model: {modelName if available}. Rebuild the collection after selecting a supported model.
```

不得记录 API Key、完整用户文档内容或完整向量。

#### 测试清单

- 1024 维向量正常写入；
- 1536 维向量在第一次 Upsert 前失败；
- 返回向量数量少于文本数量时失败；
- 中间块失败时结果为部分成功；
- 所有块失败时结果为失败；
- 取消操作抛出 `OperationCanceledException`；
- UI 不再把部分成功累计到成功数。

#### 完成标准

- 用户能区分成功、部分成功、失败；
- 维度错误不会产生任何对应块记录；
- 日志和 UI 都能定位到文件及块序号；
- 构建与 RAG 测试通过。

---

### 阶段 3：实现文档级替换和稳定键（RAG-004/RAG-005）

难度：中级开发者实施，高级开发者评审数据一致性方案。  
目标：重复摄取同一文档后，不保留旧版本孤儿记录，也不覆盖同文档内合法重复块。

#### 设计要求

引入文档清单抽象，例如：

```csharp
public interface IRagDocumentCatalog
{
    Task<RagDocumentManifest?> GetAsync(string collectionName, string documentId, CancellationToken ct);
    Task SaveAsync(RagDocumentManifest manifest, CancellationToken ct);
    Task DeleteAsync(string collectionName, string documentId, CancellationToken ct);
}
```

`RagDocumentManifest` 至少保存：

- `CollectionName`；
- `DocumentId`；
- `DocumentUri`；
- 文档内容哈希；
- 当前记录 Key 集合；
- Embedding 模型标识和维度；
- 更新时间。

具体持久化实现必须先评审。不要在业务服务中临时手写 SQL 或把清单只放进内存。

#### 操作步骤

1. 定义稳定的 `DocumentId`。本地文件建议先规范化绝对路径（调用 `Path.GetFullPath`，统一目录分隔符；Windows 下按不区分大小写处理），再计算哈希；不要直接把路径明文放进 Key，也不要使用文件内容作为文档身份。
2. 修改所有块 Key，使其包含：文档 ID、块类型、全局顺序、内容哈希前缀。
3. 完整解析并向量化新版本，得到 `newKeys`。
4. 读取旧清单得到 `oldKeys`。
5. 写入新记录。
6. 删除 `oldKeys.Except(newKeys)`。
7. 只有前述步骤成功后才更新清单。
8. 删除文档时，根据清单删除全部记录并移除清单。
9. 设计失败恢复：如果写入新记录成功但清单更新失败，下次摄取必须能重新收敛，而不是永久遗留脏数据。

#### 测试清单

- 同一文件重复摄取两次，记录数不增长；
- 删除原文中一个段落后再次摄取，旧段落不再能召回；
- 同一文件出现两个同名标题，两者都存在且 Key 不同；
- 文件内容不变时可安全重复执行；
- 删除文档后，其所有记录均不可召回；
- 中途失败时旧版本仍可用，或系统明确标记为失败待恢复。

#### 完成标准

- 文档更新后向量库中不存在旧块；
- 同内容、同类型但位于不同顺序的块不会互相覆盖；
- 文档删除有公开服务入口和测试；
- 一致性方案通过高级开发者评审。

---

### 阶段 4：保留候选元数据并扩展上下文（RAG-006/RAG-007）

难度：中级开发者主导。  
目标：Reranker 和上下文扩展阶段都能使用完整记录。

#### 建议内部模型

```csharp
public sealed record RagSearchCandidate(
    TextParagraph Record,
    double VectorDistance,
    string MatchedQuery);
```

`IRetrievalOrchestrator` 的外部返回类型可以暂时保持 `TextSearchResult`，但内部不要提前转换。

#### 操作步骤

1. 搜索结果先转换为 `RagSearchCandidate`，保留完整 `TextParagraph`。
2. Reranker 输入改为 `RagSearchCandidate`。
3. 时效评分优先使用 `PublishedAt`，只有缺失时才从 URL/正文推断。
4. 去重优先使用 `Record.Key`；内容级去重可以使用 `ContentHash` 作为第二层策略。
5. 新增 `IContextExpansionService`，按 `DocumentId + Order` 获取命中块前后记录。
6. 首版窗口固定为前 1 块、后 1 块，并设置总字符或 token 上限。
7. 同一 Section 内优先扩展；跨章节时停止，避免拼入无关段落。
8. 表格块优先补充最近标题；标题块优先补充后一个正文块。
9. 完成排序和扩展后，再转换为 `TextSearchResult` 供现有 Agent 工具使用。

#### 测试清单

- `Section`、`Order`、`PublishedAt` 在重排前后不丢失；
- 命中表格时能带上表头或章节；
- 不跨 `DocumentUri` 扩展；
- 不跨主要 Section 扩展；
- 多个命中窗口重叠时不会重复输出；
- 扩展后结果不超过配置的 token 上限。

#### 完成标准

- 检索内部链路不再以 `TextSearchResult` 作为核心数据模型；
- 时效评分可读取真实 `PublishedAt`；
- 上下文扩展行为可配置、可测试、有上限。

---

### 阶段 5：收敛图片与“多模态”设计（RAG-008/RAG-009）

难度：中级开发者实施，高级开发者确定产品边界。  
目标：只保留真实可用的能力，避免哈希向量和同维度造成错误语义。

#### 推荐短期方案

保留“图片 -> Caption -> TextEmbedding -> 文本检索”，暂不宣称跨模态检索。

#### 操作步骤

1. 修改注释、日志和 UI 文案，将能力名称改为“图片说明文本检索”。
2. CLIP 加载或推理失败时：
   - 不生成可检索的哈希语义向量；
   - 将图像向量标记为不可用；
   - Caption 仍可成功时，允许通过文本向量召回。
3. 评估 `ImageEmbedding` 是否还有实际消费者：
   - 若没有，先停止写入，后续迁移时再移除字段；
   - 若要保留，增加明确的可用状态，不用零向量伪装成功。
4. 只有在文本查询也使用与图像编码器配套的 CLIP 文本塔，并完成融合检索测试后，才能恢复“跨模态检索”描述。

#### 测试清单

- CLIP 模型缺失时不会产生哈希语义向量；
- Caption 成功时仍可通过 `TextEmbedding` 检索图片块；
- Caption 和 CLIP 都失败时，摄取结果明确包含失败信息；
- 普通文本块不需要写入 1024 维零图像向量，前提是连接器允许可空字段。

#### 完成标准

- 文档、代码注释和产品文案对能力描述一致；
- 哈希向量不参与语义检索；
- “跨模态检索”只有在共享模型空间和真实查询链路存在时才使用该名称。

---

### 阶段 6：收敛 PDF/DOCX 解析链（RAG-010）

难度：中级开发者实施，高级开发者评审解析策略。  
目标：减少 `PDF/DOCX -> Markdown -> Markdig` 的重复结构推断。

#### 实施原则

不要同时重写 PDF 和 DOCX。建议先选择结构更明确的 DOCX 作为试点。

#### DOCX 试点步骤

1. 为当前 `DocxMarkdownConverter` 建立回归样本：标题、列表、表格、图片、段落顺序。
2. 让 `DocxBlockReader` 直接遍历 OpenXML 元素并产出 `DocumentBlock`。
3. 保证全局 `Order` 单调递增。
4. 标题样式映射为 `HeadingBlock`；表格映射为 `TableBlock`；图片保存后映射为 `ImageBlock`。
5. 保留旧转换器作为短期 feature flag 回退路径。
6. 对相同样本比较新旧块序列，不以 Markdown 文本完全一致为目标，而以结构和可检索内容不退化为目标。

#### PDF 后续步骤

PDF 缺乏稳定语义结构，不应简单照搬 DOCX 方案。需要先定义可接受的提取目标：

- 页面正文顺序；
- 标题启发式；
- 表格是否要求结构化；
- 图片是否要求提取；
- 扫描 PDF 是否支持 OCR。

若现有 PdfPig 启发式无法达到目标，应评估成熟解析组件，而不是继续叠加正则和字体阈值。

#### 完成标准

- DOCX 直接输出 `DocumentBlock[]`；
- 新旧路径有同一批回归样本；
- 可通过开关回退；
- PDF 在完成选型前不做大规模复制式重构。

---

### 阶段 7：配置化查询词表（RAG-011）

难度：初级开发者可在中级开发者指导下执行。  
目标：词表可维护、配置无效时安全失败或回退。

#### 操作步骤

1. 定义强类型配置：
   - `QueryRewriteOptions`；
   - `RerankerOptions`。
2. 将同义词、停用词、金融关键词、时间关键词迁移到项目统一配置机制。
3. 使用 `IOptions<T>` 注入，不要在服务内直接读取文件。
4. 对配置做启动校验：
   - 同义词 key 不为空；
   - 权重范围有效；
   - Reranker 各主权重之和为 1；
   - n-gram 最小值不大于最大值。
5. 保留代码内默认值，配置缺失时行为与当前版本一致。
6. 增加配置绑定和无效配置测试。

#### 注意事项

配置化只解决维护问题，不等于 Query Rewrite 质量提升。词表增删必须由离线评估结果支撑。

#### 完成标准

- 修改词表不需要改服务代码；
- 配置错误在启动或加载时给出明确消息；
- 缺省配置下原有测试继续通过。

---

### 阶段 8：建立离线检索评估（RAG-013）

难度：初级开发者整理数据，中级开发者实现指标。  
目标：任何检索策略调整都能量化比较。该阶段必须先于混合检索实施，但最小数据集可以与前述正确性修复并行准备。

#### 最小数据集

首版建议 30～50 个问题，覆盖：

- 公司财报指标；
- 股票代码和数值精确查询；
- 同义表达；
- 表格问答；
- 需要标题或前后文才能理解的问题；
- 图片 Caption 查询；
- 无答案问题。

每个问题至少标注：

- 查询文本；
- 相关文档 ID；
- 相关段落 Key 或可接受 Key 集合；
- 是否必须命中 Top 1、Top 3 或 Top 5；
- 备注和数据来源。

#### 指标

首版实现：

- Recall@1、Recall@3、Recall@5；
- MRR；
- 无答案查询的误召回率；
- P50/P95 检索耗时。

有多级相关性标注后再增加 nDCG，不要在只有二元标签时为了指标数量强行引入。

#### 基线要求

每个重构阶段记录：

```text
代码版本
Embedding 模型与维度
向量库版本
数据集版本
Recall@K / MRR
P50 / P95
失败查询列表
```

#### 完成标准

- 评估可由单条命令重复执行；
- 结果包含版本和模型信息；
- 失败查询可直接定位到预期文档/段落；
- Reranker 权重或 Query Rewrite 词表变更必须附前后指标。

---

### 阶段 9：评估混合检索（RAG-012）

难度：中级开发者调研，高级开发者做 ADR 决策。  
目标：为股票代码、精确数值和专有名词提供词法召回，不在业务层自建搜索基础设施。

#### 前置条件

必须先完成：

- RAG-001 距离方向修复；
- RAG-013 最小评估集；
- 文档级替换与删除机制。

#### 技术选型要求

候选方案必须评估：

- 中文文本和数字精确匹配；
- 本地部署能力；
- 与 .NET 10 的兼容性；
- 增量写入和删除；
- 索引一致性；
- 维护活跃度；
- 是否提供稳定 API，避免业务层手写 SQL。

#### 融合策略

首版优先使用 RRF（Reciprocal Rank Fusion），不要直接混合不可比较的原始分数：

```text
RRF(d) = sum(1 / (k + rank_i(d)))
```

初始可使用 `k = 60`，但最终值必须通过评估集确认。

#### 测试样例

- 股票代码：`600519`；
- 精确指标：`PE 15.3`；
- 公司全称和简称；
- 中英文混合术语；
- 向量相关但关键数字不匹配的干扰项。

#### 完成标准

- 形成 ADR，明确采用或暂缓；
- 混合检索在评估集上优于纯向量基线；
- 文档更新和删除能同时更新两类索引；
- 不在 `RetrievalOrchestrator` 内直接堆叠存储实现细节。

---

## 9. PR 拆分建议

不要提交一个覆盖全部阶段的大 PR。建议按以下边界拆分：

| PR | 内容 | 评审重点 |
|---|---|---|
| PR-1 | 距离语义重命名、排序修复、测试 | 所有升降序和归一化是否一致 |
| PR-2 | 摄取结果、维度校验、取消传播、UI 状态 | 是否仍存在静默成功 |
| PR-3 | 稳定 Key、文档清单、替换与删除 | 数据一致性和失败恢复 |
| PR-4 | 候选模型、元数据保留、上下文扩展 | 是否跨文档/跨章节误拼接 |
| PR-5 | 最小离线评估框架 | 数据集可重复性和指标正确性 |
| PR-6 | 图片能力收敛 | 哈希向量是否完全退出语义检索 |
| PR-7 | DOCX 直接块解析 | 结构回归和回退能力 |
| PR-8 | 强类型词表配置 | 默认兼容和配置校验 |
| PR-9 | 混合检索 ADR/实现 | 是否有量化收益和索引一致性 |

每个代码 PR 必须执行：

```bash
dotnet build MarketAssistant.slnx -c Debug
dotnet test tests/TestMarketAssistant.csproj -c Debug --filter "FullyQualifiedName~TestMarketAssistant.Vectors"
```

涉及数据结构或重大架构改动时，执行全量测试：

```bash
dotnet test tests/TestMarketAssistant.csproj -c Debug
```

---

## 10. 开发者检查清单

### 编码前

- [ ] 已阅读根目录和 `src/MarketAssistant.Rag/AGENTS.md`；
- [ ] 已确认当前 PR 只处理一个阶段；
- [ ] 已为错误行为补充失败测试；
- [ ] 已确认 NuGet API 在当前版本真实存在，没有根据其他连接器文档猜测。

### 编码中

- [ ] 使用 `Distance`/`Similarity` 精确命名，不使用含糊的 `Score`；
- [ ] 异步 API 传播 `CancellationToken`；
- [ ] `OperationCanceledException` 不被吞掉；
- [ ] 不记录密钥、完整文档、完整向量；
- [ ] 不在 ViewModel 或业务服务中自建 retry、缓存、搜索框架；
- [ ] 不用随机 Key 代替稳定身份；
- [ ] 不把部分成功当作完全成功。

### 提交前

- [ ] 解决方案构建通过；
- [ ] 相关单元测试通过；
- [ ] 集成测试失败已区分环境原因和代码原因；
- [ ] 新增行为有验收测试；
- [ ] 日志和错误消息能定位文件、块序号和错误类型；
- [ ] 未提交 API Key、用户文档或本地数据库；
- [ ] 报告和代码中的能力描述保持一致。

---

## 11. 风险与回滚

| 风险 | 预防措施 | 回滚方式 |
|---|---|---|
| 排序修复改变线上结果顺序 | 集成测试加离线评估基线 | 保留旧排序 feature flag 仅用于短期对比，不长期双轨 |
| 摄取结果接口改动影响 UI | 先修改接口和测试，再修改唯一调用方 | 单 PR 内原子修改，不保留两个含义不同的入口 |
| Key 规则变化导致存量记录不可管理 | 版本化 Collection 或执行明确重建 | 删除新版本 Collection，切回旧 Collection |
| 文档替换中途失败 | 文档清单、幂等 Key、失败恢复测试 | 保留旧清单，重试同一文档摄取 |
| 解析器重构造成结构退化 | 固定样本、旧路径开关、块序列对比 | 切回 Markdown 转换路径 |
| 混合检索引入双索引不一致 | 统一写入编排和删除流程 | 暂停词法召回，保留向量索引为主链 |

向量 Schema、Embedding 模型或 Key 规则发生不兼容变化时，优先创建新 Collection 并重建，不要原地修改存量数据库后寄希望于自动兼容。

---

## 12. 最终验收标准

完成 P0 和 P1 后，RAG 模块至少应满足：

1. 余弦距离越小的候选不会在任何阶段被错误降权；
2. 非 1024 维文本向量在写入前失败，并给出清晰错误；
3. 块级失败不会被 UI 报告为文件完全成功；
4. 同一文档重复摄取不会持续累积旧块；
5. 文档删除后相关记录不再被召回；
6. 同一文件的重复标题、列表、表格和图片不会因 Key 相同被覆盖；
7. Reranker 能使用 `PublishedAt`，上下文扩展能使用 `Order` 和 `Section`；
8. 图片能力被准确描述为 Caption 文本检索，哈希向量不参与语义排序；
9. 所有关键行为均有自动化测试；
10. 每次检索策略调整都能通过统一评估集量化比较。

---

## 13. 总结

当前模块不是需要推倒重写，而是需要按正确顺序修复边界和数据语义。

最重要的工程原则是：

- 先保证距离方向正确，再讨论排序优化；
- 先保证摄取结果可信，再讨论召回增强；
- 先保证文档更新可收敛，再增加第二套索引；
- 内部保留完整领域数据，最后一步才转换为通用搜索结果；
- 不把“维度相同”误认为“语义空间相同”；
- 不以测试数量代替检索质量指标。

初级开发者可以承担基线、配置化、测试样本和验收测试；中级开发者负责距离修复、摄取结果、候选模型、上下文扩展和解析器试点；文档级一致性、存储选型和真正的跨模态方案必须经过高级开发者评审。