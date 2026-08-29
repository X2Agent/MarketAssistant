# Func 注入收敛为具名工厂 + 无意义注释清理（已逐项验证）

## 已验证的事实基础

- 7 个 `Func<MarketType, T>` 注册（App/ServiceCollectionExtensions.cs:61-74），8 个 ViewModel 消费，调用点全部传 `_marketContext.CurrentMarket`。
- 4 个服务（HomeAsset/Favorite/AssetHistory/AssetCache）用 `[ServiceKey] MarketType` 按市场参数化实例（FavoriteService.cs:24 等 4 处），**必须保留按市场解析**——不能改直接注入。
- 页面 Func ×7（:85-91），MainWindowViewModel.cs:17-23 消费，`Func<TradingPageViewModel>? = null` 为坏味道。
- SettingsPageViewModel 注入 4 个懒加载 Func（:34-42），用于向量化与保存确认。
- `Func<MarketMonitor>`（App.Services:381）打破循环依赖，TradingEnvironmentService.cs:17 消费。
- Rag `Func<IRagIngestionService>`（Rag/Extensions:58）。
- 测试桩仅 tests/Application/HomeSearchViewModelTest.cs:30 一处。

---

## 第一部分：Func 注入收敛

### 1. `Func<MarketType, T>` ×7 → `IMarketServiceRegistry`
- 新建接口（App.Services Applications 层），7 个强类型方法，各带 `MarketType` 参数：`GetKLineService / GetAssetInfoService / GetNewsUpdateService / GetHomeAssetService / GetAssetHistoryService / GetFavoriteService / GetAssetCacheService`。
- 实现内部 `GetRequiredKeyedService<T>(marketType)`；注册到 `AddApplicationServices()`，删除 7 个 Func 注册。
- 8 个 ViewModel 的 `Func<MarketType, T>` 参数统一替换为该接口（AssetPageViewModel、AssetSelectionPageViewModel、PriceAlertPageViewModel、FavoritesPageViewModel、HomeSearchViewModel、HotAssetsViewModel、RecentAssetsViewModel、TelegraphNewsViewModel）。
- 更新 HomeSearchViewModelTest 的桩为假注册表。

### 2. 页面 ViewModel Func ×7 → `IPageViewModelFactory`
- 新建 `IPageViewModelFactory`（`T Create<T>() where T : ViewModelBase`），App 层实现。
- MainWindowViewModel 注入单一工厂，删 7 个 Func 字段与可选 Func 参数；`NavigationService.NavigateTo<TViewModel>` 改用该工厂（消除其 IServiceProvider 服务定位）。
- 删 `AddViewModels` 中 7 个 Func 注册，页面 Transient 注册不变。

### 3. SettingsPageViewModel 4 Func → 2 具名接口
- `IRagInfrastructureProvider`（GetEmbeddingFactory/GetVectorStore/GetIngestionService）：实现保留延迟解析语义；删 `Func<IEmbeddingFactory>`、`Func<VectorStore>` 注册。
- `IMarketMonitorProvider`（GetMonitor）：替换 `Func<MarketMonitor>`，TradingEnvironmentService 与 SettingsPageViewModel 切换；循环依赖打破语义不变。

### 4. 顺带修复服务定位器
- `ClipImageEmbeddingService.cs:68`：移除 IServiceProvider 依赖（实现时按 `_chat` 实际用法选聊天工厂或窄接口注入）。
- `InvestmentSelectionWorkflow.cs:86-92`：新建 `IInvestmentExecutorFactory`（按 MarketType 提供 CriteriaExecutor 及其余 3 个 Executor）替代每次 Run 的服务定位。
- 同步清理该文件的"【学习要点】【实现细节】"教学式注释。

### 5. 保留不动的合理 Func
重试包裹（GlobalExceptionHandler/ViewModelBase/ToolExecutor/SqliteServiceBase）、`ThrottledExecuteAsync`、`TradeExecutor.ConfirmationRequested` 事件、`TextChunkingService` 策略参数、测试桩。

---

## 第二部分：无意义注释清理（Func 收敛后进行）

按 AGENTS.md 规范（仅函数级文档注释 + 晦涩逻辑说明）：

- **A. 方法名翻译式 /// summary（~200 处）**：`Agents/Tools/Abstractions/*.cs`（~30 处）、`AnalysisEnums.cs` 枚举成员单词翻译（~50 处）、`NewsEventAnalysisResult.cs`、9 处 `/// 构造函数`、各 ViewModel/Service 复述式 summary、`DocxMarkdownConverter.cs`。
- **B. 复述下一行代码的行注释（~70 处）**：`CryptoAssetInfoService.cs`（~10 处）、`KLineChartView.cs`（~8）、`TelegraphNewsViewModel/HotAssetsViewModel`（~11）、`BinanceMarketDataService`、`ScreenInvestmentTargetsExecutor` 等零散处。
- **C. 教学式注释**：`ClipImageEmbeddingService.cs` 的【学习要点】【实现细节】段。
- **保留**：并发/释放原理等高质量说明、设计依据注释（如"对齐设计系统裁决 #6"）、Colors/TextStyles.axaml 设计系统注释；#region 本轮不动。

---

## 验证与执行顺序

1. Func 收敛分步（1→2→3→4），每步 `dotnet build MarketAssistant.slnx -c Debug`。
2. 注释清理按项目分批（Agents → App.Services → App → Rag/DataProviders），完成后再次 build。
3. DI 改动面大，最终跑一次 `dotnet test tests/TestMarketAssistant.csproj -c Debug`。
4. 不做 git 提交（未获要求）。