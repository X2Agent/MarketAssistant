# MarketAssistant.Agents — AGENTS.md

AI Agent 抽象层，定义分析师基类、工具接口、分析模型、Token 管理和提示词配置。依赖 `MarketAssistant.Core`，使用 Microsoft Agent Framework (MAF)。

---

## 目录结构

```
MarketAssistant.Agents/
├── Analysts/
│   ├── AnalystAgentBase.cs            ← 分析师抽象基类（继承 DelegatingAIAgent）
│   └── Attributes/
│       ├── RequiredAnalystAttribute.cs   ← 标记必需分析师
│       └── RequiresToolsAttribute.cs     ← 声明分析师所需工具接口
├── MarketAnalysis/
│   └── Models/                        ← 分析结果模型（各维度分析结果 + 综合报告）
│       ├── MarketAnalysisReport.cs    ← 最终报告：符号、分析师消息、CoordinatorResult
│       ├── CoordinatorResult.cs       ← 聚合结果：评分、评级、目标价、风险
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
│   └── ConversationCompressor.cs      ← 超限时压缩历史对话
└── Tools/
    ├── Abstractions/                  ← 工具接口定义（市场无关）
    │   ├── IBasicDataTools.cs         ← 基础数据（基类）
    │   ├── IShareBasicTools.cs        ← A 股基础数据
    │   ├── ICryptoBasicTools.cs       ← 加密货币基础数据
    │   ├── IFinancialTools.cs         ← 财务数据（基类）
    │   ├── IShareFinancialTools.cs    ← A 股财务
    │   ├── ICryptoMetricsTools.cs     ← 加密货币指标
    │   ├── ISentimentTools.cs         ← 情绪数据（基类）
    │   ├── IShareSentimentTools.cs    ← A 股情绪
    │   ├── ICryptoSentimentTools.cs   ← 加密货币情绪
    │   ├── ITechnicalDataTools.cs     ← 技术分析数据
    │   ├── INewsDataTools.cs          ← 新闻数据
    │   ├── IStrategyTools.cs          ← 策略管理
    │   └── ITradingExecutionTools.cs  ← 交易执行
    └── Models/                        ← 工具返回值模型
        ├── AssetQuoteInfo.cs, MarketInterval.cs, NewsItem.cs
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
4. 分析师的具体实现位于 `MarketAssistant.App/Agents/Analysts/`，本项目只提供基类和属性。

### 工具接口扩展

1. 接口定义在 `Tools/Abstractions/`，市场特定接口继承基类接口（如 `IShareBasicTools : IBasicDataTools`）。
2. 每个工具接口需暴露 `GetFunctions()` 方法，返回 MAF 可调用的函数列表。
3. 返回值模型放在 `Tools/Models/` 对应市场子目录。
4. 具体实现位于 `MarketAssistant.App/Agents/Tools/`，注册为 Keyed Service。

### 分析模型扩展

- 新增分析维度时，在 `MarketAnalysis/Models/` 添加结果类。
- 结果类应包含结构化字段（非自由文本），便于聚合评分。
- 评分遵循 `ScoringStandards` 定义的 1–10 标准。

### Token 管理

- `TokenEstimator` 用于估算消息 Token 数。
- `ConversationCompressor` 在对话超限时自动压缩——保留最近消息，LLM 摘要旧消息。

---

## 测试

- 本项目的类型多为抽象基类和模型，测试集中在 `tests/TestMarketAssistant.csproj`。
- `TokenEstimator` 和 `ConversationCompressor` 可独立单元测试；分析师行为通过集成测试验证。

---

## 构建

```bash
dotnet build src/MarketAssistant.Agents/MarketAssistant.Agents.csproj -c Debug
```
