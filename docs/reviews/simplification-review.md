# 核心四项目简化规范审查报告

- **日期**：2026-11-17
- **范围**：`MarketAssistant.Core`（19 文件）、`MarketAssistant.Trading`（17）、`MarketAssistant.Agents`（73）、`MarketAssistant.App.Services`（253），共约 362 个 `.cs` 文件
- **方法**：规则基线预读（根 + 各项目 `AGENTS.md`、`Directory.Build.props`）→ 定向 grep 扫描（`ArgumentNullException`/`ThrowIfNull`、catch-吞异常、null-forgiving、lock/retry）→ 5 分片并行深审 → 高危项逐条人工复核（含分片冲突裁决）
- **判据**：根 `AGENTS.md`「简化优先四原则」（A1 防过度防御 / A2 入口单次校验 / A3 拒绝提前抽象 / A4 控制复杂度）+ App.Services「空与失败可区分」强制约定 + 可空注解一致性（C 类）
- **编译基线**：`Nullable=enable` + `TreatWarningsAsErrors=true`（`Directory.Build.props` 全局生效）
- **性质**：只读审查，未修改任何代码

## 总览

| 定级 | 数量 | 说明 |
|------|------|------|
| 高（确定性违规） | **9** | 违反明文强制约定，行为层面已造成语义损害 |
| 中（疑似/需讨论） | **~40** | 违反原则但存在设计权衡，修复前建议确认意图 |
| 低（风格建议） | **~45** | null-forgiving 冗余、`??` 冗余、可空标注不一致等 |
| 已复核澄清 | 12 | 初查命中但经复核属白名单/有意设计/允许例外，不计违规 |

**分布结论**：
- **Trading（App.Services/Trading）**：与资金安全白名单高度一致，确定性违规仅 1 处（DI null 检查）；主要问题是「失败伪装成默认值/零值」（凭证、影子绩效、对账计数、风控配置）。
- **Agents（App.Services/Agents）**：`ToolExecutor` 统一异常落点执行不彻底——A 股工具全走 `ToolExecutor`，Crypto 侧 4 个工具类手写 try/catch 或裸奔；投资选股执行器把「分析失败」伪装成「看似完整的默认结果」。
- **Applications**：`PriceAlertService` 是重灾区（取消/异常吞成 null、规则加载失败清空规则表）；币安 JSON `?? []` 与快讯 HTTP 失败返回空列表违反「空=查无、故障=抛」。
- **Services**：整体对齐最好；MCP 工具上下文的失败缓存语义有 1 处高危。
- **Agents/Trading/Core 三个契约层**：仅 2 处中等问题（YAML null 未校验、`CommissionAsset` 可空口径不一致），密度符合契约层预期。

---

## 高危问题（9 条，均已人工复核）

### 1. `TradingCredentialStore.cs:42` — A2 — DI 构造函数 null 检查

```csharp
public TradingCredentialStore(
    IUserSettingService userSettingService,
    ILogger<TradingCredentialStore> logger)
{
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
```

根 `AGENTS.md` 明文禁止「DI 构造函数 `?? throw new ArgumentNullException`」。`IUserSettingService` 参数未检查、`ILogger` 却检查，同属不一致。**修法**：直接赋值 `_logger = logger;`。

### 2. `PriceAlertService.cs:510-517` — A4 — 取消与数据源故障均吞成 null

```csharp
catch (OperationCanceledException)
{
    return null;
}
catch (Exception ex)
{
    Logger.LogWarning(ex, "获取A股价格失败: {AssetCode}", assetCode);
    return null;
}
```

`GetAShareLatestQuoteAsync` 上方 503 行已用 `data is null` 表达「停牌/无报价」语义，此处的 null 使「CLS 数据源故障」与之混同；且取消被吞违反「保留取消语义时 `catch (OperationCanceledException) { throw; }`」的强制约定。**修法**：OCE 直接 `throw;`，其余异常包装 `FriendlyException` 上抛，由调用方按规则隔离。

### 3. `PriceAlertService.cs:866-872` — A4 — 规则加载失败静默清空规则表

```csharp
catch (Exception ex)
{
    Logger.LogWarning(ex, "加载价格预警规则失败");
    lock (_syncRoot)
    {
        _rules = [];
    }
}
```

数据库故障后用户全部预警静默失效，UI 无从区分「零条规则」与「加载失败」。**修法**：加载失败向外抛出（构造路径 fail-fast），或保留旧 `_rules` 快照并告警。

### 4. `AnalyzeAssetsExecutor.cs:39-46` — A4 — 工作流内部不一致伪装成结果对象

```csharp
var originalRequest = input.OriginalRequest;
if (originalRequest == null)
{
    _logger.LogError("[步骤3/3] 缺少原始请求信息");
    return CreateDefaultResult("分析失败：缺少原始请求信息");
}
```

`OriginalRequest` 为 null 属上游工作流状态损坏（内部不变量被破坏），应 `throw InvalidOperationException`；返回 `CreateDefaultResult` 产出一个 UI 看似合法的「分析结论」。

### 5. `AnalyzeAssetsExecutor.cs:98-104` — A4 — AI 响应解析失败伪装成结果对象

```csharp
var result = LlmJsonExtractor.Deserialize<InvestmentSelectionResult>(response.Text, JsonOptions);
if (result == null)
{
    _logger.LogWarning(...);
    result = CreateDefaultResult("解析分析结果失败");
}
```

反序列化失败被替换为默认结果对象继续走完正常返回路径；同方法内 110 行对「校验失败」走的是 `throw InvalidOperationException`，同一文件内两种失败处理不一致。**修法**：统一为抛 `FriendlyException`。

### 6. `CryptoStrategyTools.cs:28-88`（4 个方法同模式） — A4 — Tool 方法手写 try/catch

```csharp
try
{
    return await _strategyService.GetStrategiesByStatusAsync(StrategyStatus.Active, cancellationToken);
}
catch (Exception ex) when (ex is not FriendlyException)
{
    _logger.LogError(ex, "获取活跃策略列表失败");
    throw new FriendlyException($"获取策略列表失败: {ex.Message}", ex);
}
```

项目强制约定「Tool 方法统一通过 `ToolExecutor.ExecuteAsync` 执行（日志、计时、FriendlyException 转换集中处理），不要在每个 Tool 方法里手写 try/catch」。A 股侧 `AShareBasicTools`/`AShareFinancialTools`/`AShareNewsTools` 等全部合规，本文件 `GetActiveStrategiesAsync`/`GetStrategyAsync`/`UpdateStrategyStatusAsync`/`CreateGuardrailAsync` 全部手写。**修法**：改走 `ToolExecutor.ExecuteAsync("...", ..., _logger, async _ => ...)`。

### 7. `CryptoTradingExecutionTools.cs:106-129` — A4 — 撤单失败吞成 `return false`

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "取消订单失败: {Symbol} {OrderId}", symbol, orderId);
    return false;
}
```

方法内 113-119 行「紧急停止拒单 → `return false`」是有意且带日志语义的；但 catch-all 使**网络故障、订单已成交、权限不足**与「halt 拒单」对 Agent 完全不可区分——Agent 只会认为「撤单没成功」并可能重试已成交订单。**修法**：异常包装 `FriendlyException` 上抛（由 `ToolExecutor` 转换），halt 分支保留 `return false`。

### 8. `McpToolContextProvider.cs:69-99` — A4 — 刷新失败返回已失效的旧工具缓存

```csharp
if (_invalidated)
{
    // 仅使活动连接映射失效。旧 Agent 工具仍持有底层 Client...
    await _mcpService.ResetConnectionsAsync(cancellationToken);
    _invalidated = false;
}
...
catch (Exception ex)
{
    _logger.LogError(ex, "McpToolContextProvider 加载 MCP 工具失败");
    _cachedTools ??= [];
    return _cachedTools;
}
```

时序缺陷：`ResetConnectionsAsync` 已执行（连接已重置）后 `GetAIToolsAsync` 抛异常时，`_cachedTools ??= []` 不触发（旧值非 null），返回的是**底层连接已被重置的陈旧工具集**。首次加载失败则伪装成「无 MCP 工具」。**修法**：进入刷新前先置空 `_cachedTools`；失败直接上抛。

### 9. `CryptoTradingExecutionTools.cs` 等 Crypto 侧 4 个工具类 — A4 — 未统一走 `ToolExecutor`

`CryptoTradingExecutionTools`（数据查询方法）、`SessionSearchTools.cs:21`、`KnowledgeGraphTools.cs:21`、`MemoryManagementTools.cs:22` 的 Tool 方法均未包 `ToolExecutor`，与 A 股侧模式不一致，异常处理/计时/`FriendlyException` 转换不统一。**修法**：与 #6 一并统一到 `ToolExecutor.ExecuteAsync`。

---

## 中危问题（按项目分组）

### App.Services/Trading（11 条）

| 位置 | 原则 | 问题 | 建议 |
|------|------|------|------|
| `TradingCredentialStore.cs:131-134` | A4 | 解密/读盘失败 → `return new ConcurrentDictionary<...>()`，「凭证损坏」伪装成「未配置」 | 失败上抛或返回可区分失败态 |
| `TradingCredentialStore.cs:149-152` | A4 | `Save()` 失败仅记日志，UI 以为密钥已持久化 | 向调用方传播或返回 bool |
| `StrategyPerformanceService.cs:88-91` | A4 | 绩效加载失败 → `BuildShadow(strategyId, [])`，「加载故障」伪装成「零笔影子成交」，污染胜率统计 | 抛出或可区分失败 |
| `PositionReconciliationService.cs:60-64` | A4 | 对账失败 → `return 0`，「修正数 0」与「对账失败」不可区分 | 抛出或返回负值/结果类型 |
| `LiquidationRiskEvaluator.cs:208-211` | A4 | 风控配置加载失败 → `return new RiskConfig()`，静默改用全默认阈值 | 失败告警或沿用上次快照 |
| `TradeExecutor.cs:179-186` | A4 | 幂等回查 catch → `null`，网络故障与「订单不存在(-2013)」混同 | 按 Binance 错误码分支化 |
| `TradeExecutor.cs:490-493` | A2 | `instrumentSymbol` 仅非空才规范化，空串/非法未在入口统一拒单 | 入口单次校验：空/非法即拒单 |
| `MarketMonitor.cs:698-714` | A3+A4 | 手写 `Deserialize<GridTradingParams>`（`StrategyParamsCache.cs:74` 已有解析器）+ 坏 JSON 吞成 `false`（破网检测静默失效） | 复用 `StrategyParamsCache`，解析失败显式跳过并告警 |
| `StrategyEngine.cs:279-282 / 404-407 / 484-487` | A4 | 三处策略参数 JSON 坏 → `return (false, ...)`，「配置损坏」与「条件未满足」混同 | 统一走参数缓存入口并区分失败 |
| `AISignalStrategyExecutor.cs:464-467` | A4 | 持仓查询失败 → 返回「（获取持仓信息失败）」占位文案，AI 会把它当事实写进提示词 | 失败时中止本次决策或注入显式失败标记 |
| `CryptoPortfolioService.cs:165-168` | A4 | `catch (HttpRequestException) → null`，单资产失败与「无数据」混同 | 限定 catch 类型并按批量逐项隔离约定处理 |

### App.Services/Agents（14 条）

| 位置 | 原则 | 问题 | 建议 |
|------|------|------|------|
| `AnalysisAggregatorExecutor.cs:125-133` | C | `readySuccesses!` / `readyFailures!` 4 处 null-forgiving（锁内已赋值，流分析可证明非空） | 局部变量绑定去 `!` |
| `AnalysisAggregatorExecutor.cs:204/283` | C | `message.Text!`（前置 `IsAnalystTextMessage` 已收窄） | 局部 `string text` 绑定 |
| `AnalysisAggregatorExecutor.cs:45` | A1 | `displayNameByAgentName ?? new Dictionary<...>()` 防御性 coalesce | 构造参数改非可空 |
| `CoordinatorExecutor.cs:154` | C | `coordinatorMessage.Text ?? string.Empty`，LLM 空输出与解析失败语义混同 | 空文本走 parseFailed 分支 |
| `MarketChatSession.cs:115-118` | C | 构造函数 4 个 `?? string.Empty`（providerId/modelId/endpoint/fingerprint） | 工厂处保证非空，形参改 `string` |
| `AnalystArtifactStore.cs:34` | C | `content ?? string.Empty`——签名若非可空则 `??` 是死代码；若可空则掩盖「写空文件」 | 删除 `??` 或上游 fail |
| `CryptoOnChainTools.cs:164-174, 221` | C | 连串 `?? string.Empty` 抹平「字段缺失」语义 | DTO 字段改 `string?` |
| `AnalyzeAssetsExecutor.cs:53` | A4 | 「未筛选到资产」与「分析失败」共用 `CreateDefaultResult`，UI 无法区分引导空态与故障 | 空态与失败分型 |
| `AnalyzeAssetsExecutor.cs:129-133`、`GenerateCriteriaExecutor.cs:86`、`ScreenInvestmentTargetsExecutor.cs:52` | A4 | 执行器内 catch → LogError → 包装 `FriendlyException`（log+wrap 双重处理） | 若 workflow 层已有统一转换则删 catch |
| `MarketAnalysisWorkflow.cs:293` | A4 | `_logger.LogError(ex, …); throw;`——「只记日志后原样 throw」 | 删 catch 或改带上下文包装 |
| `CryptoTradingExecutionTools.cs:57-67` | A4 | 市价取价 catch 吞掉后依赖 `price<=0` 二次检查（吞掉异常再自查，绕一圈） | 让取价异常直接上抛 |
| `LayeredMemoryContextProvider.cs:52` | A4 | 记忆注入失败 → `return []`，静默当「无记忆」 | 记警告并上抛或注入失败标记 |
| `MarketChatSession.cs:492` | A4 | 会话自动保存失败仅 LogWarning 完全吞掉 | 可观测化（回调/状态）或有限重试 |
| `AnalyzeAssetsExecutor.cs:55` 对照 | — | `IAssetDataFormatter` 为 Keyed 市场变体（合规，见澄清区） | 不改 |

### App.Services/Applications + Infrastructure（9 条）

| 位置 | 原则 | 问题 | 建议 |
|------|------|------|------|
| `BinanceFuturesAccountService.cs:74, 97` | C/A4 | `ReadFromJsonAsync<List<...>> ?? []`——响应体为 JSON `null` 时「持仓/挂单异常」伪装成「空列表」（解析失败本身会抛，窗口窄但持仓属资金敏感数据） | `?? throw new FriendlyException(...)`（与同文件 66 行订单解析一致） |
| `BinanceAccountService.cs:443` | C/A4 | 现货挂单列表同上 `?? []` | 同上 |
| `CryptoTelegramService.cs:43-46, 52-55, 105-107` | A4 | HTTP 失败 `return result`、反序列化 null 当「无快讯」、`catch { return string.Empty; }` 无类型吞时间戳解析 | 故障抛 `FriendlyException`，空列表仅表「正常无快讯」 |
| `AShareTelegramService.cs:43-46, 51-54, 112-114` | A4 | 与 Crypto 侧同源三连：HTTP 失败/解析失败/时间戳 catch 全部伪装成空数据 | 同上 |
| `CryptoAssetInfoService.cs:144-147` | A4 | `tickers == null || Count == 0 → return []`，接口故障与「无热门」混同 | 故障由数据源方法抛出，此处只判空 |
| `WebSearchService.cs:37-40` | A4 | 搜索 `catch (Exception) → []`，且未区分取消 | 至少 `catch (OCE) { throw; }`，失败与无命中分型 |
| `IndicatorAlertEvaluator.cs:151-154` | A4 | 指标计算失败 → `null`，与「K 线样本不足」混同（后者是 AGENTS.md 明文要求跳过的合法场景） | 故障抛出或可观测计数 |
| `CryptoKLineService.cs:66-72` | C | `arr[0]!.GetValue<long>()` 等 7 处 null-forgiving（`Count < 11` 校验后仍强转） | 缺元/类型不符 skip 或抛 |
| `CryptoTickTradeService.cs:56-64` | A4/C | 时间戳解析失败 → `return DateTime.Now` 伪造成交时间 | skip 该笔或整批失败 |

### App.Services/Services（4 条）

| 位置 | 原则 | 问题 | 建议 |
|------|------|------|------|
| `ChatSessionPersistenceService.cs:196-199` | A4 | FTS5 搜索失败 LogWarning 后 `return results`，「索引故障」伪装成「无匹配」 | 上抛或返回可区分结果 |
| `ReportArchiveService.cs:188-191` | A4 | 摘要行 `report_json` 损坏 → 维度分静默保持 0，与真实零分不可区分 | 标记摘要不可用或跳过该行 |
| `ServiceCollectionExtensions.cs:556-564` | A4 | 日志目录创建失败 `Console.Error` 后继续挂 File Sink（目录不可用，文件日志静默失效） | fail-fast 或降级仅 Console |
| `UserMemoryService.cs:198-203` | A4 | `catch (Exception ex) { Logger.LogError(...); throw; }`——「只记日志后原样 throw」 | 删 try/catch 直接上抛 |

### Agents / Trading / Core 契约层（2 条）

| 位置 | 原则 | 问题 | 建议 |
|------|------|------|------|
| `AnalystPromptLoader.cs:82` | A2 | `Deserializer.Deserialize<AnalystPromptConfig>(yaml)` 结果未判 null 直接访问 `config.Name`（YAML 空文档 → NRE 风险） | 边界单次校验 + `InvalidOperationException` |
| `TradeRecord.cs:32` | C | `CommissionAsset { get; set; } = string.Empty` 与 `IExchangeClient.cs:153`（`ExchangeOrderResult.CommissionAsset` 为 `string?`）口径不一致；下游 `TradeExecutor.cs:859` 又 `?? string.Empty` 补丁 | 统一为 `string?` 或钉死「未返回即 null」约定 |

---

## 低危问题（风格与可维护性，约 45 条）

### null-forgiving `!` 冗余（控制流已保证非空，可局部变量/模式匹配消除）

- `MarketMonitor.cs:113-125`：13 个 `= null!` 延迟初始化字段（测试用 protected 构造惯例，可改可空字段 + 测试注入）
- `MarketMonitor.cs:784`（`updated.MaxExecutions!.Value`）、`TradeExecutor.cs:586/1019`、`AISignalStrategyExecutor.cs:254/262`（`decision!`）、`AISignalStrategyExecutor.Guardrails.cs:187/195`、`ExchangeConditionOrderService.SpotOco.cs:80`、`TradeRecordRepository.cs:258`
- `BinanceAccountService.cs:186/230`（`filters!`）、`BinanceAuthService.cs:192-195`、`GitHubReleaseService.cs:37`、`AShareTelegramService.cs:76`（`.Select(s => s!)`）、`MCPServerConfigService.cs:62`、`AnalystTypeRegistry.cs:33`（改 `OfType<Type>()`）、`UserSettingService.cs:140`、`AnalystArtifactStore.cs:33`
- `AShareTechnicalTools.cs:100-103`（`x.O!.Value` ×4）、`MarketChatSession.cs:428`（`snapshot.AgentSessionState!.Value`）
- `Core/CryptoSymbolConverter.cs:138`（`out coinId!`——改 `out var mapped` 承接）

### `??` 掩盖/冗余（A1/C）

- `RiskManager.cs:219`（`exchangePositions?.FirstOrDefault`——接口已非可空）、`RiskManager.cs:270`（`?? []`——同上）
- `CoordinatorExecutor.cs:235/256/404`、`CoordinatorAnalystAgent.cs:42`、`CoordinatorExecutor.cs:404`（诊断日志 `?? string.Empty`）
- `MarketChatSession.cs:335/405/480`、`MarkdownReportExporter.cs:168`、`TokenEstimator.cs:20`（Agents）
- `TradeExecutor.cs:859`（`CommissionAsset ?? string.Empty`——缺字段应存「未知」而非空串）
- `StrategyPerformanceService.cs:106`（`FirstOrDefault()?.Symbol ?? string.Empty`——前置已保证非空）
- `ChatSessionPersistenceService.cs:126`、`UserSettingService.cs:53`、`TradingCredentialStore.cs:201`（`?? []`/`?? new UserSetting()`：JSON 根 null 与「全新默认」语义重叠，文档化即可）
- `AShareBasicTools.cs:62-65`（行情空字段 `?? string.Empty`——可保留 null 展示「缺失」）
- `CryptoMetricsTools.cs:303`（`response?.Tickers == null → []`——区分成功无数据与响应异常）

### 其他低危

- `TechnicalIndicatorService.cs:33-34` — A2 — 入口空校验与行情服务重复
- `AShareMarketSnapshotService.cs:79-83` — A1 — 财联社失败回退新浪（多源降级已有注释，建议补入 AGENTS.md 白名单）
- `CryptoScreenerService.cs:56-59` — A1 — CoinGecko → Binance 备源降级（同上）
- `ImageCaptionClientFactory.cs:26-29` — A4 — AI 未配置返回 null 占位（已有注释，属可接受降级）
- `AnalysisRatingAlertService.cs:80-83`、`AlertCenterService.cs:307-310`、`PriceAlertService.cs:478-480`、`MemoryExtractionService.cs:58-61` — A4 — 旁路能力吞异常（建议至少结构化可观测）
- `MarketMonitorProvider.cs:12-13`、`TradingDataService.cs:54-119` — A3 — 延迟解析/仓储门面（注释已说明意图，非必须改）
- `IReleaseService` — A3 — 单实现接口且测试未造假替身（可内联；`IAnalystArtifactStore`/`ISecureSettingsStore` 因有测试替身、`IAssetDataFormatter`/`ICriteriaGenerationStrategy` 因是 Keyed 市场变体，均不在此列）
- `SecureSettingsStore.cs:69` — A2 — `ThrowIfNull` 当前唯一调用方恒非 null（校验上移到 `UserSettingService.SaveSettings` 边界）
- `SecureSettingsStore.cs:50/71/87` — A1 — 三处 `lock` 与外层 `FileLock` 叠加（单例序列化路径，收益有限）
- `MarkdownReportExporter.cs:120` — A1 — `qm is null` 守卫仅服务反序列化缺字段场景
- `AnalystSignalParser.cs:250-256` — C — `EventNature` 默认臂静默落 `Hold`（枚举扩展会静默误映射）
- `StockSymbolConverter.cs:159-173` — A1 — 未知代码「默认 SH」与「不做默认猜测」注释并存，策略未统一
- `CryptoSymbolConverter.cs:99-112` — A2 — `ToCoinGeckoId` 与 `ExtractBaseCurrency` 连续空值判断，入口合并
- `ScenarioPresets.cs:85-91` — A4 — 坏 JSON 静默降级 null（契约注释钉死或区分）
- `LlmJsonExtractor.cs:31` — C — `T?` 返回签名与「仅空白输入返回 default」实际行为不符（改注释或签名）
- `ErrorMessageMapper.cs:73` — A1 — `NullReferenceException` 专用文案在 Nullable 工程下属防御性映射
- `SupportedMarketsAttribute.cs:20-21` — A1 — 空 `markets` 数组视为「支持全部市场」属防御性默认（应视为配置错误）
- `AnalystPromptLoader.cs` 对照 — 热重载失败回退缓存为有意设计（不标记）

---

## 已复核澄清项（初查命中、复核后不计违规）

| 项 | 澄清理由 |
|----|----------|
| `UserSettingService.cs:66-68` catch + `LogCritical` + `throw` | 有意 fail-closed：注释与日志一致表明「安全存储不可用时拒绝以空 Secret 启动」，Critical 属审计日志，非无意义 log-and-rethrow |
| `UserSettingService.cs:205-210` 6× `?? string.Empty` | 旧 JSON 迁移边界（`LegacyUserSecrets.ApplyTo`），null=字段缺失属合法边界转换 |
| `MarketChatSession.cs:283` `ThrowIfNullOrWhiteSpace` | `SendMessageStreamAsync` 对用户输入的边界单次校验，符合 A2 |
| `CryptoIndexQuoteService.cs:47-61` `SafeAsync` 吞异常 | 类注释明文「任一数据源失败仅省略对应项，全失败返回空集合（顶栏整体隐藏）」，且正确重抛 `OperationCanceledException`——文档化多源降级 |
| `IAnalystArtifactStore` 单实现接口 | `tests/MarketAnalysisWorkflowIdentityTest.cs` 有 `InMemoryArtifactStore` 测试替身 |
| `ISecureSettingsStore` 单实现接口 | `tests/Infrastructure/TestSecureStorage.cs` 有 `InMemorySecureSettingsStore` |
| `IAssetDataFormatter` / `ICriteriaGenerationStrategy<T>` 单/双实现接口 | Keyed 市场变体（`GetRequiredKeyedService`），属 AGENTS.md 允许例外 |
| `McpService` 按服务器 catch 后继续 | MCP 逐项隔离白名单 |
| `AlertRuleSymbolResolver` 预热吞异常 / 30s TTL 缓存 | AGENTS.md 明文白名单 |
| `ResolveRuntimeForRole` 吞解析失败降级默认 runtime | AGENTS.md 明文白名单 |
| `DailyBriefingService` 单次调用无重试、空输入返回 null | AGENTS.md 明文约定 |
| Trading 目录 symbol 锁、下单重试、风控 fail-closed、软约束 fail-open、对账两阶段锁、`TradingHaltState` fail-closed | AGENTS.md 资金安全链白名单（5 个 subagent 一致确认无越界防御） |

---

## 修复优先级建议

> **进度（2026-11-17）**：第一批已修复——#2/#3（含轮询循环逐项隔离配套）、#4/#5、#6、#8、#9 均已完成；#7 随 #9 的 ToolExecutor 统一一并完成（统一过程中保留手写 catch 会形成双重处理）。第二批已完成——#1（`TradingCredentialStore.cs:42` 删 `?? throw`）。高危 9 条至此全部清零。**第四批（低危）已完成**：机械清理 null-forgiving 约 22 处（`TryParse` 补 `[NotNullWhen(true)]`、模式匹配收窄、`OfType<string>`、`GetValueOrDefault` 等）、RiskManager 冗余 `?.`/`?? []` 删除、`IReleaseService` 单实现接口内联（删接口 + 4 处引用改具体类）、`SupportedMarketsAttribute` 空数组改显式抛错、`MapEventNatureToRating` 中性/未知值改返回 null（对齐「只提取、不推断」约定）、ErrorMessageMapper 移除 NRE 防御映射、多源降级补入 AGENTS.md 白名单、3 处 JSON null 语义注释钉死、`LlmJsonExtractor` 文档修正。低危中按设计保留未改的项见审查摘要说明。构建 0 警告 0 错误，测试 835 通过（与改动前基线完全一致，52 个失败均为环境依赖既有失败），格式检查通过。第三批（中危）需与维护者确认设计意图后进行。

> **补充（2026-11-21）**：中危表中 `UserMemoryService.cs:198-203` 已清理；同类的 `TradingAgentFactory.CreateAutomationAgent`「日志后原样 throw」一并删除。`AnalyzeAssetsExecutor` 的约束校验失败改为直接抛 `FriendlyException`，不再绕过自身 catch 二次包装；执行器的 log+wrap 本身保留（提供中文步骤前缀，工作流只读 `.Message`）。其余第三批条目仍待确认。

1. **第一批（高，语义损害明确）**：#2/#3 `PriceAlertService`（预警静默失效）、#6/#9 Crypto 工具 `ToolExecutor` 统一（一次性对齐 A 股模式）、#8 MCP 缓存失效语义、#4/#5 `AnalyzeAssetsExecutor` 失败分型。
2. **第二批（高，一行改动）**：#1 `TradingCredentialStore.cs:42` 删 `?? throw`。
3. **第三批（中，需确认设计意图）**：Trading 侧「失败伪装默认值」系列——建议逐条与维护者确认是否属有意降级；确认后多数可改为抛出或可区分失败态。`CommissionAsset` 可空口径建议一并统一（涉及 `TradeRecord`/`IExchangeClient`/`TradeExecutor` 三处）。
4. **第四批（低，随日常改动顺手清理）**：null-forgiving 局部变量化、`??` 冗余删除。无需专项排期。

## 审查方法学备注

- 高危 9 条全部经主审逐行人工复核（文件级证据在手）；中/低危条目来自分片深审的 file:line 证据，抽样复核 12 处（含 2 处分片冲突裁决），未发现虚报，但**修复前建议对中危条目逐一重读上下文**。
- 白名单甄别采用「注释/AGENTS.md 明文 + 测试替身/Keyed 注册实证」双证据标准，共撤销 12 条初查指控。
- 未覆盖范围：`MarketAssistant.App`（UI 层）、`DataProviders`、`Rag`、`Infrastructure` 独立项目、`tests/`。
