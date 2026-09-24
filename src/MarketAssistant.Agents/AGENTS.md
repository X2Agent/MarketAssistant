# MarketAssistant.Agents — AGENTS.md

AI Agent 契约层，定义分析师基类、工具接口、分析模型、Token 管理和提示词配置。依赖 `MarketAssistant.Core`，使用 Microsoft Agent Framework (MAF)。

---

## 目录结构

```
MarketAssistant.Agents/
├── Analysts/
│   ├── AnalystAgentBase.cs            ← 分析师抽象基类（继承 DelegatingAIAgent）
│   ├── AnalystNames.cs                ← 分析师 ASCII 名称常量（与 YAML name 一一对应）
│   └── Attributes/
│       ├── RequiredAnalystAttribute.cs   ← 标记必需分析师
│       └── RequiresToolsAttribute.cs     ← 声明分析师所需工具接口
├── MarketAnalysis/
│   ├── AnalystSignalParser.cs         ← 从分析师 JSON 输出提取机器可读信号快照（回测/绩效数据基础）
│   └── Models/                        ← 分析结果模型（各维度分析结果 + 综合报告）
│       ├── MarketAnalysisReport.cs    ← 最终报告：符号、分析师消息、AnalystSignals、CoordinatorResult、Usage
│       ├── AnalystSignalSnapshot.cs   ← 单位分析师结构化信号（评级/操作/目标价位，缺省为 null）
│       ├── CoordinatorResult.cs       ← 聚合结果：评分、评级、目标价（文本+数值）、风险
│       ├── TechnicalAnalysisResult.cs
│       ├── FundamentalAnalysisResult.cs
│       ├── FinancialAnalysisResult.cs
│       ├── MarketSentimentAnalysisResult.cs
│       ├── NewsEventAnalysisResult.cs
│       ├── AnalysisEnums.cs           ← 通用分析枚举
│       ├── ScoringStandards.cs        ← 统一评分标准（1–10 分）
│       └── AnalysisQualityMetrics.cs  ← 数据完整性与分析师共识度
├── PromptConfiguration/
│   ├── AnalystPromptConfig.cs         ← 分析师提示词配置模型
│   └── AnalystPromptLoader.cs         ← 从 YAML 加载提示词（支持热重载）
├── TokenManagement/
│   ├── TokenEstimator.cs              ← Token 估算（中文 ~1.5 字/token）
│   ├── AnalysisTokenUsage.cs          ← 单次分析的 Token 聚合（总量 + 按分析师明细）
│   ├── TokenUsageEntry.cs             ← 单条 Token 记录
│   ├── ITokenUsageRecorder.cs         ← 记录器与作用域接口
│   ├── TokenUsageRecorder.cs          ← 基于 AsyncLocal 的作用域实现（并发 run 隔离）
│   └── TokenUsageFormatter.cs         ← UI 展示格式化
└── Tools/
    ├── Abstractions/                  ← 工具接口定义（纯标记，DI 分发 key）
    │   ├── IBasicDataTools.cs         ← 基础数据
    │   ├── IFinancialTools.cs         ← 财务/市场数据
    │   ├── ISentimentTools.cs         ← 情绪数据
    │   ├── ITechnicalDataTools.cs     ← 技术分析数据
    │   ├── INewsDataTools.cs          ← 新闻数据
    │   ├── IStrategyTools.cs          ← 策略管理
    │   └── ITradingExecutionTools.cs  ← 交易执行
    └── Models/                        ← 工具返回值模型
        ├── MarketInterval.cs, NewsItem.cs
        ├── AShare/                    ← A 股数据模型
        ├── Crypto/                    ← 加密货币数据模型（含 Binance/CoinGecko/CoinDesk 响应）
        └── Technical/                 ← 技术指标模型（KDJ/MACD/BOLL/MA）
```

---

## 核心约定

### 分析师扩展

1. 继承 `AnalystAgentBase`。
2. 使用 `[RequiresTools(typeof(IXxxTools))]` 声明所需工具——`AnalystAgentFactory` 会自动按当前 `MarketType` 从 DI 解析。
3. 可选使用 `[RequiredAnalyst]` 标记为必需分析师。
4. 分析师的具体实现位于 `MarketAssistant.App.Services/Agents/Analysts/`，本项目只提供基类、属性和结果模型。

### 工具接口扩展

1. 工具接口（`Tools/Abstractions/`）为**纯标记接口**：只做 `[RequiresTools]` 声明与 Keyed DI 分发的类型 key，**不定义业务方法**。
2. 业务方法定义在具体实现类（`MarketAssistant.App.Services/Agents/Tools/`），经 `[Description]` 特性 + `GetFunctions()` 反射暴露给模型；市场差异化靠"不同实现类方法集 + keyed 注册的 marketType"表达。
3. 返回值模型放在 `Tools/Models/` 对应市场子目录。
4. 具体实现注册为 Keyed Service（如 `AddKeyedSingleton<IBasicDataTools, AShareBasicTools>(AShare)`）。

### 分析模型扩展

- 新增分析维度时，在 `MarketAnalysis/Models/` 添加结果类。
- 结果类应包含结构化字段（非自由文本），便于聚合评分。
- 评分遵循 `ScoringStandards` 定义的 1–10 标准。

### 结构化信号（回测与绩效的数据基础）

`AnalystSignalParser` 把各分析师消息中的 JSON 提取为 `AnalystSignalSnapshot`，由 `CoordinatorExecutor` 在生成报告时写入 `MarketAnalysisReport.AnalystSignals`，供 M3 策略回测与分析师绩效统计使用。

- **只提取、不推断**：某位分析师未提供评级/目标价位时对应字段保持 `null`；缺失维度禁止用默认值填充（会污染绩效统计）。
- **事件性质映射**：新闻事件分析师的 `EventNature` 按 `MajorPositive→StrongBuy / Positive→Buy / Negative→Reduce / MajorNegative→StrongSell` 等价映射为评级，统一各分析师的方向口径。
- **解析失败必须静默降级**：`LlmJsonExtractor.Deserialize` 对无法解析的文本抛 `JsonException`，`Parse` 捕获后返回 `null`——信号提取绝不能阻断报告生成。
- **失败标记与未知作者不产出信号**：`AnalystFailureMessages` 标记的消息、`CoordinatorAnalyst`（其结论以 `CoordinatorResult` 为准）与不产出方向性字段的分析师（`CryptoMetricsAnalyst`）均返回 `null`。
- 嵌套子对象（如 `Strategy`、`GrowthValue`、`ShortTermStrategy`）可能被模型输出为 `null` 或整体缺失，读取一律用 `?.`。
- 新增分析师时：在 YAML `name` 与 `AnalystNames` 注册常量，并在 `AnalystSignalParser.Parse` 的分支中补映射。

### Token 管理

- `TokenEstimator` 用于估算消息 Token 数。
- 会话压缩由 `MarketAssistant.App.Services` 中的 `ConversationCompressionMiddleware`（MAF 中间件）负责。

#### Token 消耗记录（`ITokenUsageRecorder`）

`TokenManagement/` 下的 `ITokenUsageRecorder` / `ITokenUsageScope` / `TokenUsageRecorder` 负责把每次分析实际消耗的 Token 归集到报告上，供 UI 展示。

- **作用域是唯一入口**：记录必须发生在 `using var scope = recorder.BeginScope(runId)` 打开的 `ITokenUsageScope` 内。作用域通过 `AsyncLocal` 传递，因此并发运行的分析（多个 `AgentAnalysis` 同时跑）天然互不串扰。
- **必须在 `session == null` 早返回之前记录**：`TokenTrackingMiddleware` 的既有逻辑在 `session` 为空时提前返回，而分析链路始终传 `session: null`。新增记录点务必放在该守卫**之前**，否则数据会静默丢失。
- **按角色归属**：中间件用 `innerAgent.Name` 作为归属键，该值等于分析师 YAML 配置里的 ASCII `config.Name`（例如 `FundamentalAnalyst`）。新增分析师时必须保证 YAML 的 `Name` 与工作流侧的归属字典键一致，否则消耗会被归到"未知"。
- **作用域退出后不再记录**：`Dispose` 之后的记录请求被忽略，不要在报告构建完成后再写入。
- `AnalysisTokenUsage` 是最终落到 `MarketAnalysisReport.Usage` 的聚合模型（总量 + 按角色明细）。**它是持久化模型**——新增的计算属性必须标 `[JsonIgnore]`，因为 `ReportArchiveService` 用 `JsonSerializer.Serialize(report)` 直接序列化，历史报告反序列化时 `Usage` 可能为 `null`，读取方必须容忍。
- 展示层格式化统一走 `TokenUsageFormatter`，不要在 ViewModel 里手写单位换算。

---

## 测试

- 本项目的类型多为抽象基类和模型，测试集中在 `tests/TestMarketAssistant.csproj`。
- `TokenEstimator` 可独立单元测试；会话压缩中间件和分析师行为通过集成测试验证。

---

## 构建

```bash
dotnet build src/MarketAssistant.Agents/MarketAssistant.Agents.csproj -c Debug
```
