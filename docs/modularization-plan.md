# MarketAssistant 模块化拆分方案

## 当前问题

原先所有功能集中在单一 `MarketAssistant.csproj` 中，模块间依赖不可控。

## 当前拆分结构（已实施）

```
MarketAssistant.slnx
├── src/
│   ├── MarketAssistant.Core/              # 核心抽象层
│   │   ├── Infrastructure/Core/           # MarketType, FriendlyException, ErrorMessageMapper 等
│   │   ├── Infrastructure/Extensions/     # EnumExtensions
│   │   └── Trading/Models/                # 交易模型（枚举、策略、订单等）
│   │
│   ├── MarketAssistant.Agents/            # AI Agent 抽象层
│   │   ├── Analysts/                      # AnalystAgentBase, 属性标注
│   │   ├── MarketAnalysis/Models/         # 分析结果模型、质量评估指标
│   │   ├── TokenManagement/               # Token 估算与会话压缩
│   │   ├── PromptConfiguration/           # YAML 提示词配置加载
│   │   └── Tools/                         # 工具接口定义 + 数据模型
│   │
│   ├── MarketAssistant.Trading/           # 交易抽象层
│   │   └── Abstractions/                  # IExchangeClient 接口
│   │
│   ├── MarketAssistant.DataProviders/     # 数据提供者
│   │   ├── BinanceMarketDataService       # Binance 行情/WebSocket
│   │   ├── CoinGeckoApiService            # CoinGecko API
│   │   └── CoinDeskApiService             # CoinDesk API
│   │
│   └── MarketAssistant.App/              # UI 应用层 + 实现层（入口项目）
│       ├── Views/                         # AXAML 视图
│       ├── ViewModels/                    # 视图模型
│       ├── Applications/                  # 应用服务
│       ├── Services/                      # UI 服务、MCP、导航等
│       ├── Agents/                        # Agent 工作流实现、工具实现
│       ├── Trading/                       # 策略引擎、风控等实现
│       └── Resources/                     # 样式资源
│
└── tests/
    └── TestMarketAssistant.csproj
```

## 依赖关系

```
App → Agents, Trading, DataProviders, Core
Agents → Core
Trading → Core
DataProviders → Agents, Core
```

## 拆分进度

### Phase 1: 接口隔离（已完成）
- [x] 创建 IExchangeClient 交易所抽象
- [x] 创建 AnalysisOrchestrationService 编排层
- [x] 创建 AnalystPromptLoader 配置加载

### Phase 2: Core 项目提取（已完成）
- [x] 提取 MarketType, FriendlyException 等基础类型
- [x] 提取 ErrorMessageMapper, HttpRetryHelper 等工具类
- [x] 提取 CryptoSymbolConverter, StockSymbolConverter
- [x] 提取 EnumExtensions
- [x] 提取 Trading Models（枚举和数据模型）

### Phase 3: Agents 项目提取（已完成）
- [x] 提取 AnalystAgentBase 和属性标注
- [x] 提取 MarketAnalysis Models（分析结果、质量评估）
- [x] 提取 TokenManagement（Token 估算和会话压缩）
- [x] 提取 PromptConfiguration（YAML 提示词加载器）
- [x] 提取 Tools Abstractions（工具接口定义）
- [x] 提取 Tools Models（A股、Crypto、技术指标数据模型）

### Phase 4: Trading 项目提取（已完成）
- [x] 提取 IExchangeClient 接口到独立项目
- [x] 让交易主链优先通过 `IExchangeClient` 消费交易所能力，减少对 Binance 具体服务的多头依赖

### Phase 5: DataProviders 项目提取（已完成）
- [x] 提取 BinanceMarketDataService
- [x] 提取 CoinGeckoApiService
- [x] 提取 CoinDeskApiService
- [x] 提取 BinanceWebSocketService

## 后续优化方向

### 深度解耦（未来）
当前由于模块间耦合较深，以下实现仍保留在 App 项目中：
- Agent 工作流编排（MarketAnalysisWorkflow, InvestmentSelectionWorkflow）
- Agent 工具实现（AShare/Crypto 工具，依赖 Services）
- Trading 实现（StrategyEngine, RiskManager, TradeExecutor 等）
- MarketChatSession（依赖 MCP 服务）

深度解耦需要：
1. 创建 `MarketAssistant.Contracts` 共享接口项目
2. 将 IUserSettingService、IChatClientFactory 等接口移入 Contracts
3. 将工作流编排和工具实现从 App 迁移到各自模块
4. 消除 Agents 对 Services/Applications 的直接依赖

## 近期清理结果

- 分析师发现逻辑已统一为扫描当前应用中实际加载的实现类型，避免 UI 与工作流各自维护一套发现规则。
- 分析页导航已统一改为 `AssetNavigationParameter`，旧的 `StockNavigationParameter` 已移除。
- RAG 注册入口已收敛，避免重复调用 `AddRagServices()` 带来的重复依赖注册。
- 交易账户与订单查询的主链已进一步收敛到 `IExchangeClient` 与统一的资产快照服务，减少重复包装。

## 注意事项

- 所有项目使用 `<RootNamespace>MarketAssistant</RootNamespace>`，命名空间与原始代码一致
- 所有项目（含 App 入口项目）均位于 `src/` 下各自独立目录中，互为同级
- Keyed Services 注册保持在 App 项目的 DI 配置中
- 避免循环依赖：Core 不引用任何其他模块
