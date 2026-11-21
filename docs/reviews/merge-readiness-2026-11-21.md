# 合并就绪度复核（feat/trading-and-ui-maturity）

- **日期**：2026-11-21
- **基线**：`main` @ `bc1164a`
- **分支**：`feat/trading-and-ui-maturity`（本地，未推送）
- **范围**：376 个文件、`+18763 / −8806`，来源为工作区既有的一轮重构（依赖升级、规范收敛、交易模块成熟化、界面重构、测试与文档）
- **方法**：全量构建 → 格式校验 → 脚本门禁 → 全量测试并与基线 worktree 对比 → 悬空引用矩阵 → 高危项定向审查
- **性质**：只读审查 + 1 处门禁修复（见「本轮修复」）

## 结论

**可合并（绿）**。五道门禁全绿，无悬空引用，无新增测试失败，资金安全链未削弱。

遗留项均为**已文档化的设计权衡或可选改进**，不构成阻断：

| 级别 | 数量 | 内容 |
| --- | --- | --- |
| 阻断 | 0 | — |
| 建议（合并后处理） | 2 | 中危第三批「失败伪装默认值」系列；`simplification-review.md` 的测试数字已过期 |
| 观察（不处理） | 3 | UI 需人工冒烟；`MAAI001` 实验性 API；对比度脚本口径标注 |

## 门禁数据

| 门禁 | 命令 | 结果 |
| --- | --- | --- |
| 构建 | `dotnet build MarketAssistant.slnx -c Debug` | **0 警告 / 0 错误**（`TreatWarningsAsErrors=true` 生效） |
| 格式 | `dotnet format MarketAssistant.slnx --verify-no-changes` | 退出码 0 |
| 测试（HEAD） | `dotnet test tests/TestMarketAssistant.csproj -c Debug -s tests/unit.runsettings` | **913 通过 / 0 失败** |
| 测试（基线 `bc1164a`，临时 worktree） | 同上 | **728 通过 / 0 失败** |
| 资源键 | `tools/verify-resource-keys.ps1` | 276 定义 / 275 引用，无缺失引用 |
| 对比度 | `tools/contrast-audit.ps1` | 正文与语义色均 ≥ 4.5；禁用态文本（`TextDisabledBrush`）按设计排除 |
| 文件规模护栏 | `FileSizeGuardTest` | 通过（修复后，见下） |
| 文档事实护栏 | `DocFactConsistencyTests` | 通过（命令数锚点未漂移） |

**测试增量**：728 → 913（净增 185），两侧均 0 失败，因此不存在「新增失败」或「靠删测试转绿」。

## 本轮修复（唯一代码/结构改动）

`src/MarketAssistant.App.Services/Applications/Crypto/BinanceAccountService.cs` 因新增现货 OCO 实现增至 604 行，触发 `FileSizeGuardTest`（上限 600）——这是本分支**新引入**的违规（基线 421 行）。

处理：按仓内既有模式（`ExchangeConditionOrderService.SpotOco.cs`）把 OCO 实现拆到 `BinanceAccountService.SpotOco.cs` partial 文件，主文件回到 541 行。未把新违规登记进 AllowList。修复后构建、格式、全量测试复跑全绿。

## 高危定向审查

### R1 悬空引用 — 无

| 被删对象 | 结论 |
| --- | --- |
| `IRagInfrastructureProvider` / `RagInfrastructureProvider` | 零引用 |
| `IInvestmentExecutorFactory` / `InvestmentExecutorFactory` | 零引用 |
| `IReleaseService` | 注册处已改用具体类型 `GitHubReleaseService`；仅审查文档留有描述文字 |
| `ScoreToColorConverter`、`AnalystCardViewModel`、`AnalystResultCardView` | 零代码与 XAML 引用（`Converters/README.md` 为删除说明） |
| `email.svg` / `search_empty.svg` / `wallet.svg` | 零引用；现存 `avares://` 与 `<svg:Svg>` 指向的文件均在资产目录内 |
| `SmartReader`、`Microsoft.Extensions.AI.Evaluation(.Quality)` | 全仓零引用，`Directory.Packages.props` 已无对应条目 |

### R2 被删测试的覆盖去向 — 属正确删除

`tests/InvestmentSelection/ScreenInvestmentTargetsExecutorTest.cs` 的两个用例仅断言构造函数的 `ArgumentNullException`，而被测行为（DI 参数 `?? throw`）正是本轮规范收敛**按 A2 明令移除**的对象。删除后无行为覆盖损失；`ScreenInvestmentTargetsExecutor` 仍在生产链路（DI 注册 + 工作流步骤2），其错误路径为「带步骤前缀记录 + 包装 `FriendlyException`」，与执行器契约一致。

补充建议（非阻断）：该执行器目前无行为级测试（改动前也没有），后续可补「Keyed 筛选服务按市场解析 + 上游异常转 `FriendlyException`」的用例。

### R3 规范收敛抽检 — 落地一致

- `PriceAlertService`：`OperationCanceledException` 处 `throw`；数据源故障包装 `FriendlyException` 上抛；`LoadRulesAsync` 失败不再清空规则表（注释明确「不得伪装成零条规则」）；轮询/订阅按标的与规则逐项隔离。
- `McpToolContextProvider`：刷新前 `_cachedTools = null`，失败不再返回底层连接已重置的陈旧工具集，异常自然上抛。
- `AnalyzeAssetsExecutor`：`OriginalRequest` 为空 → `InvalidOperationException`；反序列化/校验失败 → `FriendlyException`；`CreateDefaultResult` 仅用于「未筛选到资产」的合法空态。
- 工具层：`src/MarketAssistant.App.Services/Agents/Tools/**/*Tools.cs` 已无任何 `catch`，异常统一落 `ToolExecutor.ExecuteAsync`。

### R4 依赖升级 — 适配收敛，无兼容垫片

- MAF `1.19.0 → 1.28.0`、`Microsoft.Extensions.AI(.OpenAI) 10.9.0 → 10.11.0`；移除 `SmartReader`、`AI.Evaluation*` 后零残留；新增 `Microsoft.SemanticKernel.Abstractions 1.77.0` 有明确理由（Rag 直接使用 SK 类型）。
- 唯一实验性 API 使用：`Microsoft.Agents.AI.Compaction`（`MAAI001`），在 `ConversationCompressionMiddleware.cs` 内局部 `#pragma warning disable`，并已写入 `src/MarketAssistant.Agents/AGENTS.md` 的「已知例外」表。全分支仅此 1 处告警抑制，无其他 `#pragma`/`TODO`/`FIXME` 新增。

### R5 资金安全链 — 未削弱

- `LivePromotionGate` 已接入 `RiskManager`（`RiskManager.cs:173` 需要晋升、`:491` 判定），判定条件（≥30 笔影子平仓、累计盈亏转正、盈亏比 ≥1.1、回撤/盈利 ≤1.0）全部 fail-closed。
- `PositionReconciliationService` 已接入 DI 与 `MarketMonitor`（启动一次 + 每 5 分钟），两阶段锁内复核；失败按「后台循环单次失败隔离」白名单处理并向告警中心留痕，返回值无消费方误判为成功。
- 新增 `tests/Trading/*`（`LivePromotionGateTest`、`RiskManagerGateTest`、`TradeExecutorFuturesFlowTest`、`TradeExecutorResilienceTest`、`EmergencyStopServiceTest` 等）覆盖拒单、晋升、期货下单与降级分支，断言以行为结果为主，非仅「不抛异常」。

### R6 文档一致性 — 同步

根 `AGENTS.md` 新增「简化优先四原则」并补齐白名单；`Agents`/`App.Services`/`App`/`Core`/`DataProviders`/`Trading` 六处子项目 `AGENTS.md` 与代码现状一致；`docs/design.md` 与配色预览页随界面重构更新；`DocFactConsistencyTests` 通过。

## 建议项（合并后处理）

1. **中危第三批仍未处理**（`simplification-review.md` 第三批）：`TradingCredentialStore` 解密失败 → 空字典、`Save()` 失败仅记日志、`StrategyPerformanceService` 加载失败 → 零笔影子、`TradeExecutor` 幂等回查 `catch → null`、`LiquidationRiskEvaluator` 加载失败 → 全默认 `RiskConfig`。均属「失败伪装成默认值/空值」，需逐条确认是否是有意降级。方向安全性上其中多数偏 fail-closed（样本不足会阻止晋升），但「凭证损坏 = 未配置」「对账失败 = 0 修正」仍不可区分。
2. **`simplification-review.md` 的测试数字已过期**：文中「835 通过 / 52 个环境依赖失败」在本机不可复现；实测基线 728/0、HEAD 913/0。该文件是带日期的审查记录，建议保留原文，后续以本报告的门禁数据为准。

## 观察项（无需处理）

- **UI 仅验证到编译与资源键**：布局、动效、Ticker 栏、新 Trading 行组件的视觉效果需人工冒烟；这是本轮唯一无法用自动化门禁覆盖的面。
- **实验性 API 依赖**：`MAAI001`（Compaction）随 MAF 升级引入，已在文档标注，升级 MAF 后需重新确认。
- **对比度脚本口径**：`contrast-audit.ps1` 把 `TextDisabledBrush`（弱文字）从 4.5 门槛排除后仍输出「全部达标（>= 4.5）」，读起来易误解；建议输出中显式列出被排除项。

## 提交图谱

```
d2d094c [Trading] 拆分现货 OCO 下单实现以符合文件规模护栏
ccedca8 [测试] 补强应用层与架构护栏测试，同步设计文档
bba1f6f [App] 重构界面组件与视觉资源，移除旧分析师卡片视图
6fab273 [Agents] 按简化优先规范收敛异常语义与多余抽象
78e1962 [Trading] 交易模块成熟化：实盘晋升门禁、持仓对账与期货下单路径补强
e169dea [工程] 升级 MAF 与模型 SDK 依赖并新增前端校验脚本
bc1164a （main 基线）
```

拆分原则：一个文件只归一个提交；删除项随消费者走（`IReleaseService` 归 `[App]`，`IInvestmentExecutorFactory`/`IRagInfrastructureProvider` 归 `[Agents]`）。验收点为最终 HEAD，中间提交未逐一构建。
