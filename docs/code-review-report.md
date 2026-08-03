# MarketAssistant 项目代码审查与重构实施报告

> 初次审查日期：2026-07-29  
> 最新核验日期：2026-07-30  
> 核验基线：分支 `feature/multi-provider-support`，提交 `4bf670e`，并包含当时工作区未提交修改  
> 核验范围：当前工作区源码、依赖注入注册、主要调用方、测试源码；统计时排除 `bin/`、`obj/`  
> 目标读者：中级开发者可独立实施；初级开发者在代码评审下按任务卡实施  
> 行号说明：行号对应上述工作区快照；实施前应重新搜索符号，不应仅按行号定位

---

## 1. 执行结论

原报告的 34 项结论经源码核验后，分布如下：

| 核验结果 | 数量 | 处理方式 |
|---|---:|---|
| 确认问题 | 14 | 进入整改清单，采用本报告修订后的严重性和描述 |
| 部分成立 | 10 | 保留真实风险，删除错误原因、错误影响或不合理修复建议 |
| 不成立 / 误报 | 8 | 从整改清单删除 |
| 产品策略 | 2 | 由产品与发布策略决定，不作为代码缺陷 |
| **合计** | **34** | |

最重要的技术纠正：

1. .NET 内置 DI 会跟踪它创建的 disposable transient，因此原 #5 的运行时原理错误。当前应用另有独立问题：没有释放根 `ServiceProvider`，容器跟踪的资源在正常退出时仍可能得不到释放（A3）。
2. `await foreach` 的循环体顺序执行；原 #7 没有证据表明局部 `HashSet` 被并发访问。
3. ONNX Runtime 的同一 `InferenceSession` 支持并发 `Run()`；原 #8 只有懒初始化竞态成立。
4. `Interlocked.Increment` 返回每次递增后的唯一值；原 #23 不存在所述 TOCTOU 绕过。

原 34 项之外，本次为保证重构可执行性继续检查调用链，发现 3 个必须纳入计划的相邻问题：

| 编号 | 新发现 | 严重性 | 证据 |
|---|---|---|---|
| A1 | FIFO 平仓读取列顺序错误 | **Critical（启用真实交易时）/ High** | `TradingDataService.cs:419-437` 查询顺序为 `quantity, entry_price, closed_quantity`，读取却把索引 2 当 `closed`、索引 3 当 `entry`，会同时破坏可平数量、已实现盈亏和 `closed_quantity` 更新。 |
| A2 | 卖出风控在 symbol 锁外完成，锁内未复检 | **High** | `TradeExecutor.cs:90-122` 先风控，后获取 symbol 锁；两个并发卖出可基于同一持仓同时通过，随后依次下单。 |
| A3 | 根 DI 容器未在应用退出时释放 | **Medium** | `Program.cs:43` 构建根容器；`App.axaml.cs:73-87` 退出时只清理异常处理器和日志，没有释放 `ServiceProvider`。 |

当前整改阻断顺序：

1. **先修交易一致性：A1、A2、#25。** 在修复并通过并发测试前，不应启用真实交易。
2. **修复根容器所有权：A3。** 后续 ChatClient、限流器和 Singleton 的释放依赖它。
3. **修复凭据明文存储：#1，并与设置快照一致性 #11 一起实施。**
4. **修复 ChatClient 生命周期竞态：#2。**
5. 再处理取消传播、初始化、Token 估算和摘要质量等 P1 项。

---

## 2. 架构概览

```text
Core（无项目依赖）
  ↑
├── Trading → Core
├── DataProviders → Core
├── Rag → Core
├── Agents → Core, Trading
├── App.Services → Core, Agents, Trading, DataProviders, Rag
└── App → Core, Agents, Trading, DataProviders, App.Services, Rag
```

当前项目引用关系未发现循环依赖。分层方向总体合理，但“遵循洋葱架构”属于架构风格判断，不能仅凭项目引用图确认。

主要技术栈：.NET 10、C# 13、Avalonia 12.0.4、Microsoft Agent Framework 1.10.0、Microsoft.Extensions.AI 10.7.0、Semantic Kernel SQLiteVec、Serilog、CommunityToolkit.Mvvm。

---

## 3. 原 34 项逐条核验

### 3.1 原 Critical / High 项

| # | 原问题 | 核验结论 | 修订严重性 | 当前证据与准确描述 |
|---:|---|---|---|---|
| 1 | API 密钥明文存储 | **确认问题** | **High；真实交易可升为 Critical** | `UserSettingService.cs:81-82` 将整个设置对象写入 JSON；`UserSetting.cs:23,33,54,80,85,90,115` 包含 API Key、Token 和 Binance Secret Key。 |
| 2 | `ChatClientFactory` 使用已释放对象竞态 | **确认问题** | **High** | `ChatClientFactory.cs:96,137-140` 在配置切换时立即释放旧客户端；调用方持有裸 `IChatClient`，没有租约或在途请求跟踪。 |
| 3 | `MainWindowViewModel` 事件订阅泄漏 | **部分成立** | **Low / Medium** | `MainWindowViewModel.cs:52,55` 订阅长生命周期服务且未退订；但该 VM 当前只在 `App.axaml.cs:53` 创建一次。应明确为 Singleton 并在退出时退订，不应按“每次导航创建”处理。 |
| 4 | `SettingsPageViewModel` 构造函数 fire-and-forget | **部分成立** | **Medium** | `SettingsPageViewModel.cs:321` 在构造期间启动初始化，存在对象先可用、初始化后完成和无法随页面取消的问题；异常会被 `GlobalExceptionHandler` 记录，并非静默吞没。 |
| 5 | transient VM 的 `Dispose()` 不会由 DI 调用 | **不成立** | 删除；相邻问题见 A3 | .NET DI 会跟踪并释放它创建的 disposable transient，`NavigationService.cs:155,190-196,243-250` 也会主动释放出栈页面。原论断错误；`App.axaml.cs:73-87` 未释放根容器是独立的 A3，不能据此把原论断判为成立。 |
| 6 | WebSocket 事件必然导致 UI 跨线程异常 | **不成立** | 删除 | `BinanceWebSocketService.cs:163` 在接收线程发布事件合理；UI 订阅者在 `AssetPageViewModel.cs:276-286`、`FavoritesPageViewModel.cs:221-230` 主动切换到 UI 线程。 |
| 7 | Workflow 的局部 `HashSet` 存在线程竞争 | **不成立** | 删除 | `MarketAnalysisWorkflow.cs:181-295` 在单个 `await foreach` 中顺序消费，局部集合没有并发访问证据。 |
| 8 | `ClipImageEmbeddingService` 并发竞态 | **部分成立** | **Medium** | `ClipImageEmbeddingService.cs:285-297` 的懒初始化无同步；但同一 ONNX Session 可并发 `Run()`。只保护初始化，不应串行化所有推理。 |
| 9 | 后台记忆提取不可取消 | **确认问题** | **Medium** | `MarketChatSession.cs:410-420` 使用 `Task.Run` 和 `CancellationToken.None`；异常已捕获，真实问题是任务没有会话所有权和取消。 |
| 10 | RAG 摄取缺少 `CancellationToken` | **确认问题** | **Medium** | `IRagIngestionService.cs:18`、`RagIngestionService.cs:58-70,80-168` 以及 `IDocumentBlockReader.cs:10` 均缺少完整取消传播。 |

### 3.2 原 Medium 项

| # | 原问题 | 核验结论 | 修订严重性 | 当前证据与准确描述 |
|---:|---|---|---|---|
| 11 | `UserSettingService` 暴露可变引用 | **确认问题** | **Medium** | `UserSettingService.cs:21` 返回内部可变对象；`95-98` 在锁外替换对象后再保存。并发读取、属性修改与持久化没有统一同步边界。 |
| 12 | 字典 setter 缺少 key 会抛异常 | **不成立** | 删除 | `SettingsPageViewModel.cs:91` 使用字典 indexer setter；key 不存在时会新增，不会抛 `KeyNotFoundException`。 |
| 13 | 大量服务注册为 Singleton | **部分成立** | **Medium（审计项）** | `ServiceCollectionExtensions.cs:240-350` 确有大量 Singleton，但 Singleton 本身不是缺陷。桌面应用没有天然请求 Scope，应逐个审计可变状态和线程模型，禁止批量改生命周期。 |
| 14 | 两个 `ConcurrencyLimiter` 未释放 | **部分成立** | **Low** | 当前只在 `ServiceCollectionExtensions.cs:221-230` 找到一个实例。它被长期闭包捕获，生命周期等同应用生命周期；不是持续增长型泄漏。 |
| 15 | WebSocket 同步 Dispose 可能阻塞 / 死锁 | **确认问题** | **Medium** | `BinanceWebSocketService.cs:207-223` 在同步 `Dispose()` 中等待异步关闭最多 3 秒。已有 `DisposeAsync()`；`Task.Run(...).Wait()` 不是正确修复。 |
| 16 | `Program.cs` 重复实例化 `UserSettingService` | **部分成立** | **Low** | `Program.cs:35` 为日志路径临时创建实例，正式 Singleton 另行注册。会重复读取设置；并不存在“临时修改丢失”的当前事实。 |
| 17 | DI 未启用构建验证 | **确认问题** | **Low / Medium** | `Program.cs:43` 直接 `BuildServiceProvider()`，未设置 `ValidateOnBuild` / `ValidateScopes`。 |
| 18 | `GlobalExceptionHandler` 双检锁缺 `volatile` | **部分成立** | **Low** | `GlobalExceptionHandler.cs:30-36` 在 `RegisterHandlers()` 前发布 `_instance`。真实风险是初始化事务发布顺序；只加 `volatile` 不能修复事务边界。 |
| 19 | `TaskCanceledException` 一律解释为网络超时 | **确认问题** | **Low / Medium** | `MarketAnalysisWorkflow.cs:297-309` 仅排除外部 token 取消，其他内部取消仍可能被错误映射为网络超时。 |
| 20 | 错误冷却阻止配置修正后重试 | **不成立** | 删除 | `ChatClientFactory.cs:78-92` 只在配置未变化时命中冷却；配置变化会重试。 |
| 21 | `MarketContext.CurrentMarketType` 缺少 `volatile` | **部分成立** | **Low** | `MarketContext.cs:74-80` 锁内写、静态属性无同步读。枚举读取原子，且没有已发生故障证据。自动属性不能标记 `volatile`；需要时用私有字段配合 `Volatile.Read/Write`。 |
| 22 | `TradingContext.AsyncLocal` 已发生上下文泄漏 | **不成立** | 删除 | `MarketMonitor.cs:346-358` 在 `finally` 中清空，没有当前残留路径证据。 |
| 23 | `Interlocked.Increment` 后比较存在 TOCTOU | **不成立** | 删除 | `TradingFunctionGuardMiddleware.cs:45-54` 使用原子递增返回值，每次调用获得唯一序号。 |
| 24 | 流式 Token 回退估算错误 | **确认问题** | **Medium** | `TokenTrackingMiddleware.cs:68-93` 只累计字符数，再构造等长空格字符串估算，无法代表中文、英文或代码。 |
| 25 | `RiskManager` 卖出数量校验错误 | **部分成立，影响方向写反** | **High** | `RiskManager.cs:111-115` 汇总 `Quantity` 而非 `RemainingQuantity`，会高估可卖数量并可能放行超额卖出。单改该行仍不足以解决 A1、A2。 |
| 26 | `ExtractBaseAsset` 使用固定报价资产列表 | **确认问题** | **Medium；卖出校验路径可升 High** | `RiskManager.cs:12,173-181` 依赖固定后缀。更严重的是 `106-119` 在解析失败时跳过卖出持仓校验；卖出校验本来只需要完整 symbol，不应依赖 base asset。 |
| 27 | 对话摘要逐消息截断 | **确认问题** | **Medium** | `ConversationCompressionMiddleware.cs:173-180` 在摘要前把每条消息截为 500 字符；fallback 在 `206-215` 截为 100 字符，后置结论可能不可逆丢失。 |
| 28 | 文本二分递归深度等于文本长度 | **不成立** | 删除 | `TextChunkingService.cs:78,102-120` 会依次尝试语义分隔符，最后以 `null` 表示空分隔符；`144-147` 随后强制按字符中点二分。递归深度约为 `O(log n)`，原报告所述线性递归和栈溢出模式均不成立。 |

### 3.3 原 Low 项

| # | 原问题 | 核验结论 | 修订严重性 | 当前证据与准确描述 |
|---:|---|---|---|---|
| 29 | `AnalystPromptLoader` 路径固定 | **确认问题** | **Low** | `AnalystPromptLoader.cs:15-16` 固定读取应用目录 `config/prompts`。仅在需要环境覆盖时改为 Options。 |
| 30 | `TokenEstimator` 使用 `Debug.WriteLine` | **确认问题** | **Low** | `TokenEstimator.cs:24-28` 初始化失败后生产环境不可观测，并静默退化到启发式估算。 |
| 31 | `RiskConfig` 默认值硬编码 | **产品策略** | 非缺陷 | 配置模型提供默认值正常；是否允许用户修改属于产品需求。 |
| 32 | `ExchangeOrderResult.Status` 使用字符串 | **确认问题** | **Low** | `IExchangeClient.cs:72-80` 的状态、方向、类型均为字符串，跨交易所映射和大小写比较脆弱。 |
| 33 | 外部 API URL 写在 DI 注册中 | **部分成立** | **Low** | 稳定端点写在代码中不是安全问题，但降低代理、测试环境和故障切换能力。 |
| 34 | Debug-only 导航使用 `#if DEBUG` | **产品策略** | 非缺陷 | 若交易功能尚未发布，编译期隔离合理；需要灰度或动态启用时才引入 feature flag。 |

---

## 4. 重构执行规则

实施者必须遵守以下规则，避免把局部修复变成新的竞态或架构债务：

1. **一个任务卡一个 PR。** 交易一致性任务卡 T1 可作为单独紧急 PR；不要与 UI、提示词或格式化重构混合。
2. **先写失败测试，再改实现。** 如果当前类不可测试，先做最小依赖抽取，不要为了测试引入新的服务层级。
3. **不要按原编号机械修改。** “部分成立”必须按本报告的真实根因实施；“误报”不得进入代码整改。
4. **禁止批量修改 DI 生命周期。** 每个 Singleton 必须先记录：可变字段、调用线程、释放方式和所有者。
5. **取消必须向下传播，不能吞掉。** 捕获 `OperationCanceledException` 时，若调用方 token 已取消，必须重新抛出。
6. **安全失败。** 交易持仓、symbol 元数据或密钥存储不可用时应 fail-closed，不能静默跳过校验或退回明文。
7. **所有资源必须有唯一所有者。** DI 创建的服务由根容器释放；手工 `new` 的会话或客户端由创建者/租约释放。
8. **禁止把异步问题改成同步阻塞。** 不使用 `.Wait()`、`.Result`、`Task.Run(...).Wait()` 处理异步释放或初始化。
9. **不得记录密钥。** 日志、异常、测试快照和配置 diff 中不能出现 API Key、Secret 或 Token 值。
10. **每个任务卡完成后执行：**

```bash
dotnet build MarketAssistant.slnx -c Debug
dotnet test tests/TestMarketAssistant.csproj -c Debug
dotnet format --verify-no-changes
```

交易 T1、凭据 S1、ChatClient C1 属重大改动，必须运行全量测试；不能只依赖编译通过。

---

## 5. 阻断级任务卡

### R0：建立根容器所有权和退出释放

**对应：A3、#14，并解决 #5 附近真实存在的容器退出清理问题；同时是 C1 的前置条件。**

**改动文件**

- `src/MarketAssistant.App/Program.cs:28-43`
- `src/MarketAssistant.App/App.axaml.cs:18-20,28-87`
- `src/MarketAssistant.App/Services/ServiceCollectionExtensions.cs:40-44`

**实施步骤**

1. 将 `App.ServiceProvider` 的实际类型改为可释放的 `ServiceProvider`，不要只保存 `IServiceProvider`。
2. 在 `OnApplicationExit` 中按顺序执行：停止应用级后台任务、`GlobalExceptionHandler.Cleanup()`、释放根容器、将静态引用置空、最后 `Log.CloseAndFlush()`。
3. 当前所有退出事件为同步事件，先使用 `ServiceProvider.Dispose()`；若以后引入仅实现 `IAsyncDisposable` 的 Singleton，再统一设计异步关闭协调器，不要在各服务内部自行阻塞。
4. 把 `MainWindowViewModel` 注册改为 Singleton，因为当前只作为唯一主窗口根 VM 使用；同时实现幂等 `Dispose()` 退订事件。
5. 保留导航 VM 的幂等 Dispose。导航层可能先释放页面，根容器退出时再次调用，所有实现都必须允许重复 Dispose。

**禁止方案**

- 不要依赖进程退出替代 `ServiceProvider.Dispose()`。
- 不要在每个 Singleton 上注册独立的 `ProcessExit`。
- 不要为了释放 VM 把全部 ViewModel 改成 Singleton。

**测试与验收**

- 新增 DI 生命周期测试：根容器释放后，测试用 disposable Singleton 和根解析 transient 各只执行一次有效清理。
- 应用正常退出时日志中无 `ObjectDisposedException`，主窗口事件已退订。
- R0 完成后，#5 的剩余风险才算关闭。

### T1：修复卖出风控与 FIFO 平仓一致性

**对应：A1、A2、#25、#26。真实交易启用前必须完成。**

**改动文件**

- `src/MarketAssistant.App.Services/Trading/TradingDataService.cs:403-473,478-553`
- `src/MarketAssistant.App.Services/Trading/RiskManager.cs:88-119,170-182`
- `src/MarketAssistant.App.Services/Trading/TradeExecutor.cs:84-133,201-225`
- `src/MarketAssistant.Trading/TradingModels.cs:228-241`
- 新增 `tests/Trading/TradingDataServiceTest.cs`
- 新增 `tests/Trading/RiskManagerTest.cs`
- 新增 `tests/Trading/TradeExecutorConcurrencyTest.cs`

**实施步骤**

1. **先修 A1。** `ClosePositionFifoAsync` 禁止按数字索引猜列含义；使用 `GetOrdinal("quantity")`、`GetOrdinal("entry_price")`、`GetOrdinal("closed_quantity")` 读取。
2. 在同一事务内先计算 `totalAvailable = Sum(quantity - closed_quantity)`。若 `closeQty > totalAvailable`，在执行任何 UPDATE 前返回明确失败或抛出领域异常；禁止提交“只平掉一部分但上层认为全部成功”的事务。
3. `RiskManager` 的卖出校验直接按完整 `instrumentSymbol` 查询持仓，并汇总 `RemainingQuantity`。删除卖出路径对 `ExtractBaseAsset()` 成功与否的依赖。
4. `TradeExecutor` 保留锁外的初步风控和人工确认，但在用户确认后、获取 symbol 锁后，必须再次执行最终风控。第二次结果若拒绝则不得调用交易所 API；若仍要求确认且订单参数未改变，可复用本次确认结果。
5. symbol 锁必须覆盖“最终风控 → 下单 → 保存交易记录 → FIFO 持仓更新”。当前 `ExecuteApprovedOrderAsync` 已在锁内，保持该边界。
6. 单标的买入仓位计算需要 base asset 时，复用 `BinanceMarketDataService.GetExchangeInfoAsync()` 返回的 `BinanceSymbolInfo.BaseAsset/QuoteAsset`，不要继续扩充硬编码后缀。元数据不可用时，对会影响交易上限的检查 fail-closed。
7. 本任务只修正现有持久化路径，不在同一 PR 中重写整个交易存储层。若后续迁移 EF Core，应单独采用 Code First 迁移，不再扩散手写 SQL。

**必须新增的测试**

| 测试 | 场景 | 断言 |
|---|---|---|
| `ClosePositionFifoAsync_PartialClose_UsesCorrectColumns` | 数量 2、入场价 100、已平 0.5，再平 0.5，出场价 120 | 剩余 1；PnL 为 10；`closed_quantity` 为 1 |
| `ClosePositionFifoAsync_InsufficientPosition_RollsBack` | 可用 1，尝试平 2 | 无 UPDATE、无部分提交、返回明确失败 |
| `ValidateOrderAsync_Sell_UsesRemainingQuantity` | `Quantity=10`、`ClosedQuantity=8`、卖出 3 | 风控拒绝，可用量为 2 |
| `ValidateOrderAsync_UnknownQuote_DoesNotSkipSellValidation` | symbol 不在硬编码后缀列表 | 仍按完整 symbol 校验持仓 |
| `ExecuteOrderAsync_ConcurrentSells_OnlyOnePassesFinalRiskCheck` | 可用 1，并发发起两笔卖出 1 | 只调用一次交易所下单；另一笔在锁内复检被拒绝 |
| `ExecuteOrderAsync_ConfirmationWait_DoesNotHoldSymbolLock` | 第一笔等待人工确认 | 第二笔仍能进入初步流程，但不能绕过最终复检 |

**完成定义**

- 上述测试稳定重复运行 100 次无偶发失败。
- 任何路径都不能令 `closed_quantity < 0` 或 `closed_quantity > quantity`。
- 任何卖出下单前都在 symbol 锁内完成一次基于最新本地持仓的最终检查。
- 交易所余额/持仓仍作为最终权威约束；本地检查不能替代交易所拒绝处理。

### S1：将敏感凭据移出明文设置 JSON

**对应：#1、#11、#16。建议与设置快照一致性一起实施。**

**改动文件**

- `src/MarketAssistant.App.Services/Applications/Settings/UserSetting.cs:19-33,52-115`
- `src/MarketAssistant.App.Services/Services/Settings/UserSettingService.cs`
- `src/MarketAssistant.App.Services/Services/Settings/IUserSettingService.cs`
- `src/MarketAssistant.App/ViewModels/SettingsPageViewModel.cs:87-132,332-345,578-610`
- `src/MarketAssistant.App/Program.cs:32-35`
- 所有直接读取密钥的调用方：`ChatClientFactory`、`EmbeddingFactory`、`WebTextSearchFactory`、`BinanceAuthService`、`CoinGeckoApiKeyHandler`、A 股工具等
- 新增 `ISecretStore` 及平台实现；新增 `InMemorySecretStore` 仅供测试

**推荐最小契约**

```csharp
public interface ISecretStore
{
    string? Get(string key);
    void Set(string key, string value);
    void Remove(string key);
}
```

平台实现要求：

- Windows：当前用户范围 DPAPI 或 Windows Credential Manager。
- macOS：Keychain。
- Linux：Secret Service。
- 如果首个 PR 只交付 Windows 实现，非 Windows 平台必须明确禁用密钥保存并提示“不支持”，不能退回明文。

**实施步骤**

1. 先定义稳定的密钥名，例如 `llm:{providerId}`、`embedding`、`binance:api-key`、`binance:secret-key`、`web-search:{provider}`。
2. `usersettings.json` 只保存非敏感配置。序列化前使用持久化 DTO，或把敏感属性标记为不参与 JSON；不要依赖“保存前临时清空原对象”。
3. `UserSettingService.LoadSettings()` 加一次性迁移：若旧 JSON 含明文密钥，则写入 `ISecretStore`，成功后原子重写无密钥 JSON。迁移日志只记录密钥类型和数量，不记录值。
4. 如果写入安全存储失败，保留原文件并向用户报告迁移失败；不要先清空再尝试保存。
5. `UserSettingService.CurrentSetting` 返回深拷贝快照。`UpdateSettings` 在一个锁内完成：克隆输入、写安全存储、原子写非敏感 JSON、最后发布新快照。
6. 原子写文件：先写同目录临时文件，刷新成功后替换目标文件。异常时保留旧文件。
7. `SettingsPageViewModel` 编辑独立快照；只有点击保存时调用 `UpdateSettings`。保存失败时 UI 保留用户输入并显示错误。
8. `Program.cs` 不再 `new UserSettingService()`。新增只读取非敏感日志路径的 `StartupSettingsReader`，或使用固定默认日志目录完成启动。
9. 禁止将加密主密钥硬编码在代码、JSON、环境变量默认值或仓库文件中。

**必须新增的测试**

- `SaveSettings_DoesNotPersistAnySecretValue`
- `LoadSettings_MigratesLegacyPlaintextSecretsExactlyOnce`
- `Migration_WhenSecretStoreFails_KeepsOriginalFile`
- `CurrentSetting_ReturnsIndependentSnapshot`
- `ConcurrentReadsAndUpdate_DoNotObservePartialSnapshot`
- `UpdateSettings_WhenWriteFails_DoesNotPublishNewSnapshot`
- 测试使用临时目录和 `InMemorySecretStore`，不得读写真实用户密钥库。

**完成定义**

- 在测试配置中填入唯一标记字符串后，递归搜索应用数据目录、日志目录和导出文件均找不到该明文。
- 旧用户配置升级后功能可用，且 `usersettings.json` 不再包含任何密钥值。
- 安全存储不可用时保存失败并明确提示，不静默降级。

### C1：修复 ChatClient 配置切换与在途请求竞态

**对应：#2。依赖 R0；建议在 S1 的设置快照完成后实施。**

**改动文件**

- `src/MarketAssistant.App.Services/Infrastructure/Factories/ChatClientFactory.cs`
- `src/MarketAssistant.App.Services/Infrastructure/Factories/ResilientChatClient.cs`
- `src/MarketAssistant.App.Services/Infrastructure/Factories/MarketChatSessionFactory.cs:72-91`
- 所有 `IChatClientFactory.CreateClient()` 调用点
- 新增 `tests/Infrastructure/ChatClientFactoryTest.cs`

**推荐最小设计：版本化退役，应用退出统一释放。**

当前配置切换频率低，不必第一步引入引用计数租约。安全且较小的方案：

1. 定义不可变 `ChatClientConfiguration` record，包含 provider、model、endpoint 和密钥版本/值，用于完整比较。
2. 配置变化时先成功创建新客户端，再原子替换 `_cachedClient`；旧客户端加入 `_retiredClients`，不立即 Dispose。
3. 创建失败时保留当前客户端供既有在途请求完成，但新 `CreateClient()` 对新配置继续返回错误；错误冷却使用独立的 `_lastFailedConfiguration`，不能覆盖当前成功配置。
4. `ChatClientFactory` 实现幂等 `IDisposable`：根容器退出时统一释放当前客户端和所有 retired 客户端。
5. 若产品未来频繁切换模型或长期运行导致 retired 数量不可接受，再升级为 `IChatClientLease : IAsyncDisposable` 引用计数；不要在本次修复中同时改造所有 Agent 所有权。
6. 为可测试性，把“根据配置创建原始客户端”的动作抽成 internal delegate 或 internal factory 接口；不要在测试中访问真实 LLM。

**禁止方案**

- 把 `oldClient.Dispose()` 移到锁内：锁外调用方仍在使用，问题不变。
- 用 `Thread.Sleep` 等待“可能完成”的请求。
- 配置变化后永不释放所有客户端且没有根容器退出清理。
- 在异常日志中输出 API Key 或完整配置对象。

**必须新增的测试**

- `CreateClient_UnchangedConfiguration_ReturnsSameInstance`
- `CreateClient_ChangedConfiguration_ReturnsNewInstance`
- `CreateClient_ChangedConfiguration_DoesNotDisposeInFlightOldClient`
- `CreateClient_FailedNewConfiguration_KeepsOldClientAliveButDoesNotReturnItForNewCalls`
- `Dispose_DisposesCurrentAndRetiredClientsExactlyOnce`
- `CreateClient_ConcurrentConfigurationSwitch_CreatesAtMostOneClientPerSuccessfulVersion`

**完成定义**

- 旧客户端上的阻塞请求在设置切换后可正常完成。
- 新请求只使用新配置；新配置创建失败时不偷偷使用旧配置。
- 应用退出后所有成功创建的客户端恰好释放一次。

---

## 6. P1 任务卡

### P1-1：设置页显式初始化与页面级取消（#4）

**改动：** `SettingsPageViewModel.cs:26,306-354,456-571,626-662,681-693`，`SettingsPageView.axaml.cs:8-20`。

1. 从构造函数删除 `_ = SafeExecuteAsync(InitializeAsync, ...)`。
2. 将初始化改为公开、幂等的 `Task InitializeAsync(CancellationToken)`；使用缓存 Task 防止重复附加可视树时重复加载。
3. View 在 `AttachedToVisualTree` 时调用初始化；VM 暴露 `IsInitializing`，初始化完成前禁用依赖配置的命令。
4. VM 持有 `_lifetimeCts`，`Dispose()` 时取消；`FetchModels`、`VectorizeDocuments` 接收并传播 token。
5. 初始化异常由 `SafeExecuteAsync` 统一记录和展示，不使用无观察的 async void 业务方法。

**验收：** 导航离开设置页后网络模型列表请求和文档向量化可取消；重复进入不重复订阅或重复初始化。

### P1-2：后台记忆提取归属会话生命周期（#9）

**改动：** `MarketChatSession.cs:22-44,399-420,533-552`。

1. 增加会话级 `_lifetimeCts`；删除 `Task.Run`，直接启动异步方法并保存 Task 引用。
2. 提取调用使用 `_lifetimeCts.Token`，Dispose 时先取消。
3. 实现 `IAsyncDisposable` 以便可等待后台任务；同步 Dispose 只取消并做幂等内存清理，不同步等待。
4. 创建 `MarketChatSession` 的 ViewModel/工厂明确负责释放会话。

**验收：** 释放会话后提取收到取消；没有未观察异常；不会在旧会话销毁后继续写记忆。

### P1-3：RAG 摄取全链路取消（#10）

**改动范围：** `IRagIngestionService`、`RagIngestionService`、`IDocumentBlockReader` 及三个 reader、`IImageEmbeddingService` 调用、`SettingsPageViewModel.VectorizeDocuments`。

1. 所有异步接口末尾增加 `CancellationToken cancellationToken = default`。
2. 传播到文档读取、embedding `GenerateAsync`、图片 Caption/Generate、向量 `UpsertAsync`。
3. 在同步的长循环和每个 block 开始处调用 `ThrowIfCancellationRequested()`。
4. `RagIngestionService` 的逐 block `catch (Exception)` 前增加取消专用分支并重新抛出，禁止把取消记录成普通 block 失败后继续。
5. Settings 页把取消与失败分开显示；取消不计入 failed files。

**测试：** 分别在读取、embedding、图片处理、Upsert 阶段触发取消；后续 block 不再处理，调用方收到 `OperationCanceledException`。

### P1-4：只同步 CLIP Session 初始化（#8）

**改动：** `ClipImageEmbeddingService.cs:85-100,272-315` 及 Dispose。

1. 用 `Lazy<InferenceSession?>` 或私有锁保护一次性初始化。
2. Lazy 工厂内部捕获初始化异常并返回 null，避免 `Lazy<T>` 永久缓存异常导致降级路径不可用。
3. `GenerateAsync` 取得已发布的 Session 后可并发调用 `Run()`；不要加全局推理锁。
4. Dispose 仅在 `IsValueCreated` 时释放 Session，并与并发 Dispose 做幂等保护。

**测试：** 32 个并发首次调用只创建一次 Session；初始化失败只记录一次并稳定走 fallback；并发推理不被串行化。

### P1-5：WebSocket 释放不阻塞 UI（#15）

**改动：** `BinanceWebSocketService.cs:180-227` 及其所有者。

1. `DisposeAsync()` 负责协议级 Close 和等待接收循环结束。
2. `Dispose()` 只取消 CTS、原子交换 `_ws`、直接释放本地资源；不得等待网络。
3. 用 `Interlocked.Exchange` 或统一状态锁保证 Dispose/DisposeAsync 并发时只释放一次。
4. 正常可等待的调用方优先 `await DisposeAsync()`；应用崩溃或同步兜底才走 Dispose。

**验收：** UI 线程调用 Dispose 在 100ms 内返回；服务器不响应 Close 时应用仍可退出。

### P1-6：流式 Token 回退使用真实文本（#24）

**改动：** `TokenTrackingMiddleware.cs:61-96`，`TokenTrackingMiddlewareTest.cs`。

1. 用 `StringBuilder` 累积 `update.Text`，而不是只统计字符数。
2. 无 Usage 时调用 `TokenEstimator.EstimateTokens(actualText)`。
3. 有 Usage 时仍以提供商值为准；测试 Usage 可以在最后一个 update 到达。
4. 保持取消语义：枚举取消时不要把不完整输出记成完整精确值；如需记录，标记为估算。

**测试：** 中文、英文、代码、纯空格四类流式输出；回退值必须等于对拼接真实文本直接估算的结果。

### P1-7：统一 symbol 元数据来源（#26）

**改动：** `RiskManager.cs`、`BinanceMarketDataService.cs:153-165`、`BinanceMarketDataModels.cs:45-50`，以及现有 `CryptoAssetInfoService` 缓存逻辑。

1. 提取可复用 symbol 元数据查询服务，返回完整 `Symbol/BaseAsset/QuoteAsset`。
2. 复用现有 Binance exchangeInfo 和一小时缓存，不新增第二套 HTTP/缓存实现。
3. 卖出持仓检查按完整 symbol 执行，不依赖元数据解析。
4. 买入单标的仓位检查需要 BaseAsset；元数据缺失时 fail-closed 并给出可诊断错误。

**测试：** BTCUSDT、ETHBTC、FDUSD 交易对、未知 symbol、元数据接口失败。

### P1-8：摘要改为全局 Token 预算（#27）

**改动：** `ConversationCompressionMiddleware.cs:159-216` 及其测试。

1. 删除逐消息固定 500 字符截断。
2. 定义摘要输入 Token 总预算；按角色分隔构建输入。
3. 超预算时采用“保留消息头部 + 尾部结论”的 token-aware 截断，并显式插入省略标记；不要只保留开头。
4. fallback 同样保留首尾，而不是固定前 100 字符。
5. 若仍无法满足质量要求，再单独实现分块 map-reduce 摘要，不在第一版同时增加多轮 LLM 调用。

**测试：** 长代码、长表格、结论位于消息尾部、中文多轮对话；摘要请求不超预算且尾部结论仍出现。

---

## 7. P2 可维护性任务表

| 项目 | 最小实施方案 | 验收标准 | 禁止做法 |
|---|---|---|---|
| #3 主窗口生命周期 | `MainWindowViewModel` 注册 Singleton；实现幂等 Dispose 退订 `PropertyChanged` 与市场事件；由 R0 根容器释放 | 启停一次只订阅/退订一次 | 不要为此修改全部 VM 生命周期 |
| #13 Singleton 审计 | 为每个有可变字段的 Singleton 记录字段、线程、锁、释放责任；只修改有证据的类 | 审计表无“未知所有者”资源 | 禁止批量改 Scoped；桌面应用没有请求 Scope |
| #14 限流器 | R0 后再决定；如需确定释放，将 limiter 包装为 DI 管理的 Singleton holder 并由 resilience 配置引用 | 根容器退出时 holder Dispose 一次 | 不要每次请求创建 limiter |
| #16 启动设置 | 用轻量 `StartupSettingsReader` 只读取非敏感日志路径，或固定启动日志目录 | 启动只读取设置一次；不构建临时容器 | 不要直接 `new UserSettingService()` 绕过其依赖 |
| #17 DI 验证 | `BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true })` | 测试中构建完整服务集合无异常 | 不要因验证失败关闭验证，应修注册 |
| #18 异常处理器发布 | 在锁内先 `RegisterHandlers()`，成功后再赋 `_instance`；Cleanup 幂等 | 并发 Initialize 只注册一次；注册失败不发布半初始化实例 | 只加 `volatile` |
| #19 取消与超时 | 使用独立 timeout CTS，并根据哪个 token 被取消决定提示；未知取消保持取消语义 | 用户取消不显示网络超时；真实 timeout 有明确日志 | 按异常消息字符串猜全部来源 |
| #21 市场快照 | 只有确认跨线程读需求后，改私有 `int` 字段并 `Volatile.Read/Write`，或统一锁内访问 | 并发切换测试通过 | 给自动属性加不存在的 `volatile` |
| #29 Prompt 路径 | 增加 typed options 和默认 `AppContext.BaseDirectory/config/prompts` | 测试可注入临时目录 | 无产品需求时不要引入文件监控框架 |
| #30 TokenEstimator 日志 | 优先改为可注入服务；若改动面过大，先增加一次性诊断事件并由宿主记录 | tokenizer 失败在生产日志可见且不重复刷屏 | 静态类内自行创建全局 LoggerFactory |
| #32 订单状态 | Core/Trading 层引入统一枚举，保留 `RawStatus`；适配器边界完成映射 | 未知外部状态映射为 Unknown 且原值可诊断 | 在业务层散落大小写字符串比较 |
| #33 外部端点 | 使用 typed options，代码提供安全默认端点；测试覆盖自定义 base URL | 测试环境无需改源码即可替换端点 | 将 API Key 放入仓库配置 |

#31、#34 是产品策略，只有在产品需求明确后才建任务。

---

## 8. 测试覆盖与新增测试计划

### 8.1 当前测试统计

排除 `bin/`、`obj/` 后：

| 指标 | 数量 |
|---|---:|
| 测试源码 `.cs` 文件 | 44 |
| 含 `[TestClass]` 的文件 / 类 | 41 |
| `[TestMethod]` 方法 | 266 |

测试框架为 MSTest，主要使用 Moq。`tests/Vectors/ClipImageEmbeddingServiceTest.cs` 已有 7 个测试，原“无测试”结论错误。

### 8.2 必须优先补齐的测试项目

| 顺序 | 测试文件 | 目的 |
|---:|---|---|
| 1 | `tests/Trading/TradingDataServiceTest.cs` | 锁定 A1 列映射、FIFO、事务回滚和数量不变量 |
| 2 | `tests/Trading/RiskManagerTest.cs` | 锁定 RemainingQuantity 和未知 symbol 的 fail-closed 行为 |
| 3 | `tests/Trading/TradeExecutorConcurrencyTest.cs` | 证明并发卖出只有一笔通过最终风控 |
| 4 | `tests/Settings/UserSettingServiceTest.cs` | 证明密钥不落盘、迁移安全、快照一致、原子保存 |
| 5 | `tests/Infrastructure/ChatClientFactoryTest.cs` | 证明配置切换不释放在途客户端，退出时只释放一次 |
| 6 | `tests/Rag/RagIngestionCancellationTest.cs` | 证明取消不被 block 级 catch 吞掉 |
| 7 | `tests/Middleware/TokenTrackingMiddlewareTest.cs` | 覆盖真实流式文本回退估算 |
| 8 | `tests/Middleware/ConversationCompressionMiddlewareTest.cs` | 覆盖全局预算、首尾保留和 fallback |
| 9 | `tests/Vectors/ClipImageEmbeddingServiceTest.cs` | 增加并发首次初始化和失败 fallback |

测试命名统一使用 `Method_Scenario_ExpectedResult`，异步测试返回 `Task`。涉及文件系统时使用每测试独立临时目录并在 Cleanup 清理；不得访问真实用户设置或真实密钥库。

---

## 9. 删除的误报与禁止整改项

以下 8 项应从原整改计划删除：

- #5：DI 不会释放 transient disposable；当前根容器未释放应按独立 A3 处理；
- #6：WebSocket 发布者必须切换到 UI 线程；
- #7：`await foreach` 中局部 `HashSet` 必然并发访问；
- #12：字典 indexer setter 在 key 不存在时抛异常；
- #20：配置修正后仍被错误冷却阻止；
- #22：当前 `AsyncLocal` 调用链已发生泄漏；
- #23：`Interlocked.Increment` 后比较可并发绕过上限；
- #28：二分递归深度等于文本长度并导致所述栈溢出。

A3 是核验原 #5 调用链时发现的独立问题，应按 R0 修复，但不能用于证明原 #5 成立。

另外，#31 和 #34 是产品策略，不应以代码缺陷计数。

---

## 10. 分阶段验收清单

### 阶段 0：建立基线

- [ ] 记录实施时 commit 和工作区状态。
- [ ] `dotnet build MarketAssistant.slnx -c Debug` 通过。
- [ ] `dotnet test tests/TestMarketAssistant.csproj -c Debug` 通过并保存测试数量。
- [ ] 禁止在已有失败测试上开始重构，除非先记录并隔离已知失败。

### 阶段 1：交易安全

- [ ] T1 所有测试通过。
- [ ] 并发卖出压力测试重复 100 次，无双重下单。
- [ ] `closed_quantity` 始终处于 `[0, quantity]`。
- [ ] 真实交易开关在 T1 完成前保持关闭。

### 阶段 2：资源与凭据

- [ ] R0 根容器退出清理通过。
- [ ] S1 明文搜索为零，旧配置迁移可回滚。
- [ ] C1 在途请求切换配置测试通过。
- [ ] 日志与异常不包含密钥值。

### 阶段 3：生命周期与取消

- [ ] 设置页、RAG、后台记忆、WebSocket 均支持可验证取消。
- [ ] 没有 `.Wait()`、`.Result` 或无所有者 `Task.Run` 新增。
- [ ] `OperationCanceledException` 不被记录为普通失败或网络超时。

### 阶段 4：质量与维护性

- [ ] Token 回退估算使用真实文本。
- [ ] 摘要保留尾部结论并满足输入预算。
- [ ] DI 构建验证和格式验证通过。

---

## 11. 核验依据与限制

- 本报告基于提交 `4bf670e` 加当时工作区修改；报告文件本身当时为未跟踪文件。后续代码变更会导致行号偏移。
- 已检查关键调用链，但未执行真实交易、压力测试、故障注入、依赖漏洞扫描、磁盘 ACL 检查或覆盖率采集。
- 并发缺陷结论基于当前锁边界、事务边界和 .NET 运行时语义；实施后必须由任务卡中的并发测试验证。
- .NET DI disposable 行为参考 Microsoft 官方文档：<https://learn.microsoft.com/dotnet/core/extensions/dependency-injection-guidelines>。
- ONNX Runtime 同一 Session 并发 Run 结论参考官方维护者答复：<https://github.com/microsoft/onnxruntime/issues/114>。
- Windows `ProtectedData` 仅支持 Windows；跨平台构建不能把 DPAPI 当成统一实现：<https://learn.microsoft.com/dotnet/api/system.security.cryptography.protecteddata>。
- 本报告中的类型签名是设计约束，不是可直接复制的完整补丁；实施者仍需依据当前 NuGet 版本的编译器签名完成调用。

---

## 12. 最终判断

修订后的报告可以作为重构主清单使用，并已补齐中初级开发者最容易遗漏的改动顺序、所有权边界、禁止方案、测试场景和完成定义。

但有两个明确边界：

1. **初级开发者不得独立合并 T1、S1、C1。** 这三项涉及真实交易、凭据迁移和并发资源所有权，必须由熟悉 .NET 并发与安全存储的开发者评审。
2. **任务完成不以“代码已改”判断，而以任务卡测试和 Definition of Done 全部满足判断。**

当前最重要的行动不是处理全部 34 项，而是按顺序完成：`T1 → R0 → S1 → C1 → P1`。