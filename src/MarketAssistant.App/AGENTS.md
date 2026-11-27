# MarketAssistant.App — AGENTS.md

主应用入口项目（Avalonia 桌面应用）。**职责限定为 UI 宿主**：包含 View、ViewModel、导航、通知、对话框、样式资源和内容文件输出；业务逻辑、Agent 实现、Workflow、Tool、MCP、交易引擎收敛在 `MarketAssistant.App.Services`，RAG 基础能力收敛在 `MarketAssistant.Rag`。

---

## 目录结构

```
MarketAssistant.App/
├── config/
│   └── prompts/                   ← 分析师提示词配置（每个分析师一个 yaml 文件）
│       ├── FundamentalAnalyst.yaml
│       ├── FinancialAnalyst.yaml
│       ├── TechnicalAnalyst.yaml
│       ├── MarketSentimentAnalyst.yaml
│       ├── NewsEventAnalyst.yaml
│       ├── CryptoMetricsAnalyst.yaml   ← Crypto 专属指标分析师
│       ├── CoordinatorAnalyst.yaml
│       └── Crypto/                     ← Crypto 市场差异化提示词
├── Converters/                    ← AXAML 值转换器
├── Infrastructure/                ← ViewLocator、ImeCommitGuard 等 UI 宿主基础设施
├── Resources/Styles/              ← Avalonia 样式资源字典
├── Services/                      ← UI 适配服务
│   ├── Dialog/                    ← Avalonia 对话框实现
│   ├── Navigation/                ← 页面导航
│   └── Notification/              ← UI 通知
├── ViewModels/                    ← MVVM ViewModel
├── Views/                         ← Avalonia AXAML 视图
│   ├── Controls/                  ← 自定义控件
│   ├── Components/                ← 复合组件
│   ├── Pages/                     ← 页面视图
│   └── Windows/                   ← 窗口
├── skills/                        ← 输出到运行目录的 Skill/参考资源
└── Assets/                        ← 图片、图标
```

---

## UI 与样式约定（Avalonia AXAML）

- **文件格式**：视图文件使用 `.axaml` 扩展名。
- **布局约束**：
  - `Padding`/`Margin`/间距取 `4 / 8 / 12 / 16 / 20 / 24`（4 的倍数）；**页面级内边距与区块间距用 24**，卡片内边距 16–20，紧凑元素 8–12。
  - **禁止硬编码数值**：使用 `Resources/Styles/Spacing.axaml` 中的资源（如 `{StaticResource SmallMargin}`），或在 `UserControl.Resources` 中定义局部资源。
- **设计系统**：完整规范见 [`docs/design.md`](../../docs/design.md)（v3.0，当前唯一生效）。四条底线：单一强调色 `#0066FF`、不画边框（靠色阶分层）、全局五档字号（32/24/16/14/12，`BodySm` 是高密度场景的降级档）、等宽字体只给数字。
- **字重**：一律用 `{DynamicResource WeightTitle|WeightEmphasis|WeightBody|WeightNumber}`，禁止 `FontWeight="Bold"` 等字面量（暗色模式需自动降一档，字面量不会跟随主题）。C# 创建的控件同样适用：用 `this[!Property] = new DynamicResourceExtension("...")` 绑定。
  - **中文回退字体只有 400 / 700 两档**：需要稳定的两档对比（未读 / 已读）时用 `WeightBody` 对 `WeightTitle`，不要拿 `WeightEmphasis`(500) 当中间档——500 会随字体缓存里先加载的是哪一档落到 Regular 或 Bold（`docs/design.md` 3.2）。对比两张离屏渲染图时先排除这一点。
  - **`DataTemplate` / `ControlTemplate` 里，带排版类（`body-sm` / `caption` / `num*` / `title` 等）的文字不要再把字重或颜色写成会被类样式盖掉的本地值**：`DataTemplate` 里是本地 `{DynamicResource}`；`ControlTemplate` 里除 `{Binding}` 外的任何本地值（字面量 / `StaticResource` / `DynamicResource` / `TemplateBinding`）都会被盖掉，且编译运行都不报错、只是静默退回类样式的取值（模板外则本地值胜出，行为不一致）。在 `UserControl.Styles` 写具名类，再用 `Classes` / `Classes.xxx="{Binding ...}"` 套上去（范例：`TextBlock.alert-unread`、`TextBlock.balance-muted`、`TradeMonitorView` 的 `near-limit`、`HomePageView` 的 `news-title`、`AssetSelectionPageView` 的 `rec-emphasis`）；取值相同的本地值直接删掉。`tests/Architecture/TemplateStyleOverrideGuardTest` 会扫全部 `.axaml` 并给出 `文件:行号`，失败就按此修，不要放宽测试；完整优先级实测表见 `docs/design.md` 3.2。
- **样式管理**：统一遵循 `Resources/Styles/` 中的集中式样式资源，避免硬编码颜色与字体。
- **资源引用**：
  - v2 的 `Primary*` / `Info*` 键**已全部删除**，没有别名兼容层；强调色一律用 `AccentBrush` / `AccentHoverBrush` / `AccentPressedBrush` / `AccentSubtleBrush`。
  - 主题感知资源 → `{DynamicResource}`；固定资源 → `{StaticResource}`。**不要在 C# 里用 `FindResource` 取字符串键**：v3 删除键后它静默返回 `UnsetValue`（曾导致加载点阵整体不可见）。
  - 改动样式或资源键后跑 `tools/verify-resource-keys.ps1`，它会同时检查 XAML 标记扩展与 C# 字符串键。
- **控件**：优先使用 Avalonia 内置控件。
- **动效**：四种性格不可混用，参数与边界见 `docs/design.md` 第六章。开关圆点（`KnobPill`）
  与收藏烟花（`CelebrationBurst`）属于「性格 D：物理」，**禁止把缩放/位移回流到按钮
  hover/press**——那是性格 A 的地盘，会让工具栏抖动。
- **选择控件**：有「保存」按钮的页面用 `CheckBox`，改动立即持久化才用 `ToggleSwitch`；
  互斥选项一律 `RadioButton`。判定表与全库盘点见 `docs/design.md` 7.12。
  `ToggleSwitch` 外观由 `ToggleSwitchStyles.axaml` 全局接管，使用点不要再写宽度/配色
  （行内尺寸用 `Width="48"` 即可）。
  - 开关的**危险方向必须二次确认，安全方向必须零确认**（如交易监控：开启确认、关闭直接停）。
  - 绑定用 `IsChecked="{Binding X, Mode=OneWay}"` + `Command`，不要双向绑定：命令里可能有确认框
    或异步落库，双向绑定会在取消后留下错误的开关视觉；取消分支要 `OnPropertyChanged` 回推。
  - 开关放在**列表行内**时，若行容器是 `ListBoxItem`（有整行选中），外层要包一个 `Border` 接 `PointerPressed` 并 `e.Handled = true`，再显式执行命令，否则点击会冒泡被 `ListBoxItem` 吃掉并触发整行选中（进而打开编辑表单）。行容器是 `ItemsControl` + `Border`（无整行选中）时不需要这层拦截。
- **页签**：全库只有一套实现 `Button.underline-tab`（`ButtonStyles.axaml`），选中态 = 强调色文字 + 底部 2px 指示条。`Classes.selected` 绑 VM 的 `IsXxxTabSelected`，点击走 `Command` + `CommandParameter`。**不要**每页各写一套页签 `ControlTemplate`，也不要用 `TabControl` 自己改 `TabItem` 模板（会带回 Fluent 的胶囊与焦点方框）；页签栏固定在内容区顶部，不放进页面的 `ScrollViewer`。规则见 `docs/design.md` 7.13。
- **二级页面的页面级操作**（如 AI 分析的 `⋮` 更多操作）放进主窗口返回条右侧的操作槽：`MainWindow.axaml` 返回条里的 `ContentControl` 按页面 ViewModel 类型用 `DataTemplate` 注入控件（参考 `AgentAnalysisMenuButton`），与「← 标题」同一行，按钮用 `btn-ghost btn-icon`。**不要在页面内另起一整行只放一个图标按钮**，那会让顶部多出一条几乎全空的横带。槽位末尾 `ViewModelBase` 的兜底空模板必须保留：全局 `ViewLocator` 匹配所有 `ViewModelBase`，没有它其余二级页面会被整页渲染进返回条。
- **区块底色**：卡片与区块用 `CardBackgroundBrush` / `SurfaceVariantBrush`。`SelectedBackgroundBrush` 的语义是"这一项被选中"，只能用在可选中元素的局部（导航项、列表项、行内小选中块），**不要拿它铺满整张卡片**；`accent` 浅底同理用 `AccentSubtleBrush`。
- **状态 / 方向 / 环境标签一律用 `StatusTag`**（`Views/Controls/StatusTag.axaml`）：`Text` + 语义类（默认中性；`info` / `success` / `warning` / `danger` / `bullish`（红）/ `bearish`（绿）），用 `Classes.warning="{Binding IsXxx}"` 切换，同时带多个类时声明靠后的生效。底 = 12% 语义底，字 = **配对文字令牌**（`XxxPanelTextBrush` / `BullishTagTextBrush` / `BearishTagTextBrush`）；**不要把 `BullishBrush` / `WarningBrush` 等语义主色直接写在 12% 底上**（对比度只有约 3:1），`tools/contrast-audit.ps1` 会校验这些配对。不要再手写 `Border` + `TextBlock` 标签，也不用语义色实底压白字。
- **按钮层级（单主按钮）**：同屏（左右分栏、固定底栏、抽屉一并算）只有一个 `btn-primary`，其余用 `btn-secondary`（`SurfaceVariantBrush` 底）；设置页的辅助操作用 `btn-info`。主次随状态切换时用互斥的条件类 `Classes.btn-primary="{Binding !IsEditing}"` + `Classes.btn-secondary="{Binding IsEditing}"`（参考 MCP 页「添加服务器」）。实底 `success` / `warning` 按钮**已下线**，不要重新引入——成功 / 警告语义交给 `StatusBanner` 与语义图标。规则见 `docs/design.md` 2.1。
- **整块可点用 `Activation`**（`Views/Controls/Activation.cs`）：卡片、`Border`、`AssetCardView` 整块点击一律 `controls:Activation.Command` + `controls:Activation.CommandParameter="{Binding}"`，元素自己声明 `Focusable="True"`；它同时覆盖点击与 Enter / Space，执行前检查 `CanExecute`。**不要**再写 `Tapped` + `KeyDown` 两个代码后置处理器，也不要用 `Border.Tag` 传条目。`DataTemplate` 里回到页面命令用 `$parent[UserControl].((vm:XxxViewModel)DataContext).XxxCommand`。
- **语义提示用 `StatusBanner`**（`Views/Controls/StatusBanner.axaml`）：`Severity`（`Info` / `Success` / `Warning` / `Danger`）+ `Message`，右侧 `Content` 是操作槽（如 `重试`）；可见性在使用点用 `IsVisible` 绑定。页面不要再手写内联的语义色提示条；只有它装不下的多段结构（交易监控的紧急停止横幅）才直接用 `XxxPanelBackgroundBrush` / `XxxPanelTextBrush` 手写。
- **危险确认**：删除（告警 / 会话 / MCP 服务器 / 交易策略）/ 清除密钥 / 平仓 / 切换实盘这类不可撤销操作用 `IDialogService.ShowConfirmationAsync(..., destructive: true)`：接受钮 `btn-danger`，默认焦点与 `Esc` 落在取消钮，**不得出现 `btn-primary`**；资金相关的人审确认再加 `topmost: true`。确认文案要写清连带后果（删除策略会撤销它在交易所的条件单；清除密钥会连带清除共享密钥的配对模式），确认框放在 `SafeExecuteAsync` 之前。自动交易批准走 `ShowTradeApprovalAsync(TradeApprovalRequest)`（`TradeApprovalWindow`），只改展示——**fail-closed 语义（超时 / `Esc` / 关闭窗口 / 无可用窗口 = 拒绝）由 `DialogService` 与 `TradeConfirmationService` 保证，样式改动不得触碰**。规则见 `docs/design.md` 7.10。
- **行内折叠与明细行**：卡片内的次要内容用 `Expander Classes="inline"`（不要裸 `Expander`，会画出两个描边框）；行内"数量 · 价格 · 手续费"明细用 `WrapPanel Classes="meta"` + 每字段一个 `TextBlock`（按字段隐藏、整段换行，不再省略号截断数字）。
- **按钮焦点环**：`ButtonStyles.axaml` 的 `:focus-visible` 会临时关闭 `ClipToBounds` 并画 `FocusRing`（`Button` 默认裁剪，环会被裁成四个角），`FocusAdorner` 全局置空；新增按钮变体不要自己再写焦点样式。祖先链上尺寸紧贴按钮的 `ClipToBounds=True` 容器（`ItemsControl` 默认就是）同样会把环裁掉上下两边，按钮行用 `ItemsControl` 生成时要写 `ClipToBounds="False"`（范例：`MessageDialogWindow`）。
- **条目自绘状态的 `ListBox` 加 `plain-list`**（`ListStyles.axaml`）：Fluent 把悬停 / 选中底色画在 `ContentPresenter#PART_ContentPresenter` 上，只改 `ListBoxItem.Background` 不生效。导航、MCP 服务器卡、聊天侧栏这类"条目自己负责视觉"的列表必须加它，否则选中时会露出 Fluent 的整块蓝底。条目里的卡片若带 `AccentRing`（向外延伸 4px），卡片四周要留 `TinyMargin`，否则选中环被 `ListBoxItem` 裁成只剩底边。
- **图标**：状态 / 箭头 / 关闭 / 刷新这类要随主题着色的小图标用 `Resources/Styles/Geometries.axaml` 的 `StreamGeometry` + `PathIcon`（`Foreground` 绑主题令牌）；**不要用 Emoji 或字形字符（✅ ⚠ ⇄ ✕ ↑ ↓）**，ViewModel / 服务层拼出来的状态文案同样适用。固定配色的功能图标才用 `Assets/Images/` 的 SVG，一个语义一个图标，不重复存。
- **资产**：非必要不改动图片与资产文件。

### 加载态与状态浮层（强制约定）

页面/区块的加载、空、失败三态必须走统一机制，禁止每页自行手写提示面板。

- **状态源**：`ViewModelBase` 提供 `State`（`LoadState`）、`IsInitialLoading`、`OverlayState`、`OverlayMessage`、`ErrorMessage`、`EmptyMessage`、`RetryCommand`。
  - 加载方法一律通过 `protected Task LoadAsync(Func<CancellationToken, Task<bool>> loader, string operationName, ...)` 编写：loader 返回 `true` → `Ready`，`false` → `Empty`，抛 `OperationCanceledException` → `Idle`，其他异常 → `Failed`（经 `ErrorMessageMapper` 转可读文案）。
  - 构造函数末尾用 `SetRetryCommand(() => LoadXxxAsync())` 注册重试入口，再 `_ = LoadXxxAsync();` 触发首屏加载。
- **状态浮层**：统一使用 `Views/Controls/StateOverlayView`，绑定 `OverlayState` / `OverlayMessage` / `RetryCommand`。该控件**自管理 `IsVisible`**（加载/空/失败时可见），页面无法从 XAML 外部关闭它——只能通过改变绑定的 `State` 来控制。
  - `OverlayState` 而非 `State`：已有内容时刷新不显示浮层，避免内容闪烁。
  - 重试按钮仅在 `Failed` 且 `RetryCommand` 非空时出现（`CanRetry`），空态不会误显示"重试"。
- **骨架屏优先于转圈**：首屏加载展示骨架屏（绑定 `IsInitialLoading`），同时把内容区反相门控为 `{Binding !IsInitialLoading}`。骨架屏存在时必须给 `StateOverlayView` 加 `ShowLoading="False"`，否则浮层的加载点阵会叠在骨架上。
  - **不要拿 `ProgressBar` 冒充转圈**：横向 `ProgressBar` 的主题 `MinWidth` 是 200，`IsIndeterminate` + `Width="16"` 实测仍渲染成 200×16 的横条（首页快讯标题栏、简报弹窗都踩过）。"进行中"用骨架、`LoadingDots` 或一句文案表达，标题栏不放加载指示器；`ProgressBar` 只当横向进度条用。
  - **进行中文案的省略号统一用三个英文句点 `...`**（`思考中...` / `正在加载...` / `AI 分析中...` / `生成中...`），不要混入 `…`（U+2026）：同一类文案在不同页面显示的省略号宽度不一样，会让界面显得不统一。
- **失败必须看得见，且与"空"可区分**：内容区用 `IsReady`（或 `!HasError`）门控，失败时让位给 `StateOverlayView` 的失败视图；只用 `!IsInitialLoading` 门控会把 `Failed` 状态下的默认值当成真实数据展示（设置页、策略配置页都踩过）。交易监控是例外——紧急停止 / 风控链路必须始终可达，失败时用页面顶部的 Danger `StatusBanner`（带重试）就地提示，其余内容照常保留。
- **不要把长耗时 AI 操作接入该机制**：AI 任务的进度（如 `AssetSelectionPageView` 运行态的内联进度卡、`AgentAnalysisPageView` 的 `ProgressDisplayView`）表达的是"任务进行中"而非"数据加载中"，继续使用 `IsBusy`；**不要用整页遮罩**（会盖住用户刚写的内容且没有取消入口），进度内联在页面里。
  - 页面自己的 `IsBusy` 若已表示"任务运行中"（如 AI 选股页），页内其它命令（星标切换等）不要用会切换 `IsBusy` 的 `ViewModelBase.SafeExecuteAsync`，改用 `GlobalExceptionHandler.SafeExecuteAsync`。

### 骨架样式

骨架屏统一使用 `Views/Controls/` 的**三个**控件（Avalonia 无内置骨架，本项目自建）。
三层各司其职，**不要互相替代**：

| 层 | 控件 | 职责 |
|---|---|---|
| 组 | `controls:SkeletonList` | 重复 N 个骨架（`Count` / `Spacing` / `Orientation`） |
| 卡 | `controls:SkeletonCard` | 一个骨架的壳 + 内容（`Variant` 对齐真实卡片） |
| 行 | `controls:SkeletonLines` | 一段等高灰条（一种字号） |

**重复必须用 `SkeletonList`，不要给 `SkeletonCard` 加 Count**：一个控件实例只能挂在一处，
把"重复"做成卡片属性就得发明 `ContentTemplate` 并写单卡/多卡特判，那是设计缺陷。

**四种写法：**

| 场景 | 写法 |
|---|---|
| 单卡单段 | `<controls:SkeletonCard Variant="Row" Row="BodySm" LineWidths="200,*"/>` |
| 单卡多段 | `SkeletonCard` 内纵向放多个 `SkeletonLines`（每段一种字号） |
| 列表重复 | `SkeletonList` + `ItemTemplate` 里放一个 `SkeletonCard`；横排用 `Orientation="Horizontal"` |
| 多列网格 | `SkeletonList` + 显式 `ItemsPanel`（`UniformGrid Columns="2"`） |

- **一个 `SkeletonLines` 只表达一段等高灰条**。真实卡片"代码 12 / 名称 14 / 价格 24"是多段、
  字号各不相同，必须纵向放多个。**不要用字符串属性编码多维结构**——那会丢掉每段的字号，
  画出一排等高的错误灰条。
- **骨架必须是真实内容的"结构克隆"**：同容器、同 `Padding`、同 `Spacing`。
- **禁止用 `MinHeight` 猜高度**。`skeleton-text` / `skeleton-title` / `skeleton-row` /
  `skeleton-card` / `skeleton-panel` 已下线，不要再引入。
- **不要手写 `<Border>` 充当骨架专用壳**，也不要复用真实卡片的样式类（`news-card` / `q-card`，
  会带上 `Cursor=Hand` 与 hover 高亮）。用 `SkeletonCard.Variant`。资产卡的骨架直接用 `AssetCardSkeleton`。
- **骨架在真实卡片内部时不要包 `SkeletonCard`**：判据是「这个壳在加载完成后还在吗」——
  还在（如 `AssetPageView` 的 `rightSection`）就说明它是真实卡片，骨架直接放进它的内容区；
  不在才说明是骨架专用壳，才用 `SkeletonCard`。
- 骨架只服务**首屏**（`IsInitialLoading`）；刷新（`IsLoading` 且已有内容）不显示骨架。
- **骨架只替换数据，不替换静态文字**：页标题、字段名、按钮文案保持真实文字。灰条颜色取 `SkeletonBrush`（`Border.skeleton` 已绑定；暗色单独取 `#262626`，否则在 0.55–0.95 的呼吸透明度下与卡片几乎同色）。
- **派生自 `ItemsControl` 的控件必须覆写 `StyleKeyOverride => typeof(ItemsControl)`**（`SkeletonList` 已覆写）：
  派生控件按自身类型找主题，找不到就没有模板、整块渲染为空；`ContentControl` 派生不受影响（`SkeletonCard` 无需覆写）。

### 资产卡片与 AI 选股页

**资产卡 `AssetCardView`**（`Views/Controls/`）：首页热门资产（`Variant=Tile`）与自选看板（`Variant=Card`）共用，
**不要再各页写一套卡片模板**；结构与视觉规则见 `docs/design.md` 7.3。

- 操作位是控件的 `Content`（一个 `btn-icon-sm`），控件会吞掉其中的 `Tapped`：页面不需要再写"阻止冒泡"的处理器，点星标不会触发"打开详情"。
- 整卡点击用 `controls:Activation.Command` / `CommandParameter`（见上文「整块可点用 `Activation`」），不要写 `Tapped` / `KeyDown` 代码后置处理器，也不要用 `Border.Tag` 传递。
- 星标状态以收藏库为准：条目 VM 持有 `IsFavorite`，页面 VM 实现 `IRecipient<AssetFavoritesChanged>`，收到广播后回显
  （消息可能来自非 UI 线程，用 `Dispatcher.UIThread.Post`）；切换命令本身不手动改标志位。热门卡按条目自己的 `MarketType` 取收藏服务
  （`GetService<IFavoriteService>(item.Asset.MarketType)`），避免市场切换瞬间写错市场的收藏库。
- 卡片只用列表数据里已有的价格与涨跌，**不为卡片追加行情请求**，不画迷你走势图。
- 骨架是 `AssetCardSkeleton`（`Variant` / `HasRank` / `HasMetric`），**改 `AssetCardView` 结构时必须同步改它**。

**AI 选股 / 选币页（`AssetSelectionPage`）**：页面三态互斥——输入态 → 运行态（内联进度卡）→ 结果态（独占内容区、从页面顶部开始），
`IsComposing = !IsBusy && SelectionResult == null`。

- `RunSelectionAsync` 是唯一运行入口：需求、新闻、快捷策略三个入口都收敛到它；进度来自服务层 `IProgress<InvestmentSelectionProgress>`（工作流真实阶段）。
- 取消静默回到输入态；失败回到输入态，顶部失败条（Danger `StatusBanner`）就地显示并给 `重试`（`RetrySelectionCommand` 重放上一次运行），**不弹全局模态框**。
- `新的选股` 回到输入态、保留输入内容、丢弃结果；离开页面（`Dispose`）与切换市场都会取消在途运行，结果不跨页保留。
- 运行进行中不接受第二次运行（每次运行都消耗 Token）。
- 视图模型按三态拆成 partial：`AssetSelectionPageViewModel.cs`（输入态、模式卡、市场切换）、`.Run.cs`（运行流程、进度、计时、取消 / 重试）、`.Results.cs`（结果列表、星标回显、跳转）。`FileSizeGuardTest` 限制单文件 600 行，新逻辑放进对应分部，不要塞回主文件。

### 输入法兼容（Windows 拼音按 Shift 提交重复）

- **现象**：拼音输入法下输入 `free` 后按 Shift（提交原始拼音），文本变成 `freefree`。成因是 Avalonia.Win32 缺陷（上游 [AvaloniaUI/Avalonia#20036](https://github.com/AvaloniaUI/Avalonia/issues/20036)，12.1.x 未修复），**所有** `TextBox` 系控件（含 `AutoCompleteBox`）都会触发，换控件无法规避。
- **绕过**：`Infrastructure/Core/ImeCommitGuard`，由 `App.OnFrameworkInitializationCompleted()` 在首个窗口显示前 `Install()`。它经 `Window.WindowOpenedEvent` 为每个窗口（含对话框、托盘恢复的主窗口）挂 WndProc 钩子，吞掉提交期间的 `WM_IME_CHAR`；新增窗口无需任何处理。
- **局限**：只覆盖 `Window` 自身的 HWND。Windows 上 `Popup` / `Flyout` / 下拉默认是独立的 `PopupRoot` 窗口，目前其中没有输入框；若要放，需另行验证 IME 提交是否仍重复。
- **退出条件**：上游修复并升级 Avalonia 后，删除 `ImeCommitGuard` 及其 `Install()` 调用。

### 可编辑 ComboBox：下拉关闭会全选输入

- **现象**：Avalonia 每次下拉关闭后都会把内部输入框全选（ViewModel 程序化收起下拉也一样），之后的输入会覆盖已键入内容。
- **绕过**：`PriceAlertPageView` 在 `DropDownClosed` 里把选区收回到末尾（选中候选项导致的关闭保持全选）。新增由代码开合下拉的可编辑 `ComboBox` 时照此处理。

### ComboBox：宽度与聚焦环

- **宽度**：控件主题默认 `HorizontalAlignment=Left`，宽度会随文字（占位符、选中项）变化。表单里必须显式写 `HorizontalAlignment="Stretch"`，与同行控件等宽。
- **聚焦环**：`FormStyles.axaml` 里 `TextBox` / `AutoCompleteBox` / `ComboBox` / `NumericUpDown` 聚焦（`ComboBox` 还含下拉展开）时临时关闭 `ClipToBounds` 并画 `FocusRing`，做法同按钮焦点环；新增输入类控件照此逐层放开外壳与模板根 `DataValidationErrors` 的裁剪。`Expander.inline` 已不裁剪，否则里面输入框的环会被裁掉三边。
- **伪类**：可编辑 `ComboBox` 的焦点会被转交给内部输入框，自身没有 `:focus`，只有 `:focus-within`；普通 `ComboBox` 展开后焦点进入弹层，自身只剩 `:dropdownopen`（Avalonia 没有 `:open`）。
- **复合控件的内部输入框**：可编辑 `ComboBox`、`NumericUpDown` 的外壳才画底色与环，内部 `TextBox` 命中全局 `TextBox` 悬停 / 聚焦 / 禁用规则时由专门规则退回透明、无环；`NumericUpDown` 的加减按钮底色是直角，聚焦时须设为透明，否则会盖住外壳圆角处的环。调整全局 `TextBox` 状态样式时要一并核对这几条。

---

## ViewModel 编码规范

- 使用 `CommunityToolkit.Mvvm` 源生成器：`[ObservableProperty]`、`[RelayCommand]`。
- `ICommand` 属性禁止在 getter 中每次创建新实例。
- 订阅外部事件的 ViewModel 实现 `IDisposable`，在 `Dispose` 中取消订阅。
- `ObservableCollection` 在构造函数中初始化一次。
- 异常处理：使用 `SafeExecuteAsync` 或 `ErrorMessageMapper`，**不要吞并异常**；加载走 `LoadAsync`，命令走 `SafeExecuteAsync`，不要在 ViewModel 里手写重复的 `try/catch`（局部降级的 UX 除外，如列表项行情补全）。
- 通过 DI 容器获取依赖，不使用 `ServiceLocator`。
- ViewModel 可以消费 `MarketAssistant.App.Services` 暴露的业务服务，但不要在 UI 层新增 Agent、Tool、Workflow 实现。
- 列表页筛选逻辑（纯函数、无 IO）抽为独立静态类（如 `AlertHistoryFilter`），ViewModel 只做装配与状态映射，便于脱离 Avalonia 单测。

### 告警历史页（PriceAlertPage → 告警历史）

- `AlertHistoryViewModel` 持有 `_allAlerts` 全量副本，可见集 `Alerts` 由 `AlertHistoryFilter.Matches` 按级别/标的关键词重建；筛选后为空与"无告警记录"是两种空态，文案必须区分（`SetEmpty` 传不同消息，不要复用同一句）。
- 历史告警的"AI 归因"按钮经 `NavigationMessage("Analysis", ...)` 跳转多分析师研判页；告警归属市场与当前市场不一致时先 `MarketContext.SwitchMarket`，否则分析链路会解析错市场的 Keyed 数据服务。系统级告警（无标的）不展示该按钮（`AlertEvent.HasSymbol` 门控）。
- `DataTemplate` 内的按钮命令经 `$parent[ItemsControl].((vm:XxxViewModel)DataContext).Command` 绑定到宿主 VM（`x:DataType` 不可跨 DataTemplate 冒泡）。
- **未读 = 左侧强调点 + `alert-unread` 字重**（`WeightTitle` 对 `WeightBody`，见上文字重约定）；级别用 `StatusTag`（提示 `info` / 警告 `warning` / 紧急 `danger`）。
- **已读时机是离开，不是进入**：`PriceAlertPageViewModel.OnSelectedTabIndexChanged`（离开历史页签）与 `OnNavigatedFrom`（带着历史页签离开页面）触发 `AlertHistory.MarkAllReadCommand`，`AlertsChanged` 同步刷新导航栏未读徽标。进入即标记已读会让用户永远看不出哪些是新的。`MarkAllReadAsync` 走 `SafeExecuteAsync`，不手写 `try/catch`。

### 告警规则页（PriceAlertPage → 规则 / 历史）

布局与配色规则见 `docs/design.md` 8.5，几条容易再犯的：

- **页签栏固定在内容区顶部**，只有规则列表在 `ScrollViewer` 内。此前整页套一个 `ScrollViewer`，滚到列表底部就切不了页签。
- **创建卡用 `CardBackgroundBrush`**，不要用 `SelectedBackgroundBrush` 铺底（那是选中项令牌，会造成整页第二个强调色焦点，并让输入框的凹陷层次失效）。
- **表单字段用"标签在上、控件在下"的网格**；主按钮与「仅提醒一次」并排成独立一行，不要塞进字段网格里当第 N 列。
- **卡片内的「高级选项」用 `Expander.inline`**（同 `StrategyConfigView` 的高级参数），`Expander.section-card` 只用于页面级分组壳。
- **标签用 `StatusTag`**（`一次性` / `已触发` = `warning`，`持续` 与市场 = 中性），不用语义色实底。
- **规则行的条件列是通用模板**，覆盖全部 15 种条件：方向箭头 + 条件名 + 目标值 + 附加条件，由 `PriceAlertRule.IsUpwardCondition` / `IsDownwardCondition` / `ConditionValueText` / `RuleOptionsText` 驱动。新增 `AlertCondition` 时同步补这几个属性，不要在 XAML 里按条件写分支。
- **删除图标是中性色**（继承按钮前景），破坏性由删除确认弹窗（`destructive: true`）承担。
- **「跌幅」是绿色、「涨幅」是红色**，这是 A 股习惯（`BearishBrush`/`BullishBrush`），不是 bug。

### 交易模块（TradingPage → 三个页签）

三个页签（策略配置 / 实时监控 / 交易历史）共享同一骨架，规则见 `docs/design.md` 8.4。页签本身用全局 `Button.underline-tab`（见 `docs/design.md` 7.13），交易页不再自带页签样式。

- **页签内标题**：`card-title` + `caption`，间距 `TinySpacing`。父级已给 `PagePadding`，页签内不重复。
- **栅格**：`PageGrid` + `ColumnSpacing="{StaticResource GridGutter}"`，常用 7:5 跨度。
  **禁止**写 `ColumnDefinitions="*,12,*"`（硬编码 gutter）或页内写死 `64` 标签列宽。
- **卡片**：一律 `CardView`（圆角 12）。筛选栏也是卡片，不要用 8 圆角的裸 `Border`。
- **行模板唯一**：`Views/Components/Trading/` 下的 `StrategyRowView` / `PositionRowView` /
  `OrderRowView` / `TradeRecordRowView`（交易历史页与策略执行历史窗口共用）。三页只引用不复制——此前同一份"符号 + 标签 + 参数"标记存在 6 份拷贝，
  内边距与圆角各不相同。`StrategyRowView.Actions` 注入操作按钮，注入内容仍在宿主
  `ItemsControl` 子树内，故按钮命令可用 `$parent[ItemsControl]` 取宿主 VM。
- **监控页布局**：状态主卡（12 列，含监控开关与紧急动作）→ 7:5 双栏。
  监控开关与紧急动作**必须在主卡内**，不要排成主卡之外的一列图标按钮。
  风控进度行的标签列用页内 `RiskMetricLabelWidth` 固定宽度，不要用 `Auto`。
- **三态**：骨架（`IsInitialLoading`，结构克隆）+ `StateOverlayView`（`ShowLoading="False"`）。
  策略配置页空列表不算无数据页（新建表单必须可操作），浮层只承担失败重试。
- **持仓数据源**：监控页 `Positions` 是 `PositionInfo`（交易所快照，含未实现盈亏/标记价/
  强平价/杠杆），不是本地 `Position` 账本；策略归属由本地账本按 `StrategyId` 补齐。
- **`ExpanderContentPadding` 是全局令牌**，不要在页面 `Resources` 里重复定义。
- **风险可见性**：交易环境是页签栏右侧常驻的 `StatusTag`（实盘 = `danger`，测试网 / Demo = `warning`；批准窗口同一规则），不要在页签内再放一份；币安数据刷新失败用页顶 `Warning` `StatusBanner`（带"重新加载"）；风控进度条逼近上限用 `near-limit` 类（`IsDailyLossHigh` / `IsPositionHigh`）；强平风险用级别 `StatusTag`，不只靠彩色圆点。
- **没有值就别画假值**：没有价格的订单（如市价单）隐藏价格字段（`ExchangeOrderResult.HasDisplayPrice` 判定），类型由"市价"标签表达；未成交没有均价、现货没有强平价——同样按字段隐藏，不显示 `0.00`。"最大回撤"取各策略的最大值而不是求和。
- **紧急停止 / 全部平仓 / 解除紧急停止与风控拒单链路属于资金安全链**：只改样式，位置与逻辑变动必须经用户确认。全部平仓与切换实盘走危险确认（`destructive: true`）。

### 每日简报（HomePage → DailyBriefingViewModel → DailyBriefingWindow）

- 简报 VM 是**例外**：纯手动生成策略下它 Singleton 注册（`AddViewModels()` 内），构造/市场切换只读当天缓存，绝不自动生成（生成会产生真实模型费用）；故 `HomePageViewModel.Dispose` 不释放它，归 DI 容器所有。
- 首页仅保留简报**入口按钮**，正文在独立弹窗 `DailyBriefingWindow` 中展示：窗口经 `IWindowService.ShowDailyBriefingWindow` 打开（统一 owner 选择与重复打开防护），ViewModel 不直接 new 窗口。
- 总开关关闭时生成按钮禁用、生成入口直接拦截并给出"已关闭"空态文案；近 30 天 Token 用量摘要展示在弹窗底部（只展示 Token 计数，货币成本估算已移除）。
- 生成中与失败/空态用骨架与 `StateOverlayView` 表达，不要把"生成中"做成阻塞按钮。
- **正文区三种状态互斥**：首次生成（还没有旧简报）只给正文骨架，不叠「暂无数据」浮层（`ShowBriefingSkeleton` / `BodyOverlayState` 门控）；重新生成保留旧正文并调淡，不再叠骨架；重新生成失败时旧正文照常可读，失败信息走顶部 Danger `StatusBanner`（带重试，`ShowRegenerateFailedBanner`），整块失败浮层只在"既无简报又不在生成"时出现。
- **简报正文走 `RichTextBlock`（Markdown 渲染）**，见下一节。

### Markdown 渲染（RichTextBlock）

- `RichTextBlock` 直接调 Markdown.Avalonia 的引擎（`Transform()`），**不经 `MarkdownScrollViewer`**。库自带样式集的每条选择器都限定在 `.Markdown_Avalonia_MarkdownViewer` 之下，而这个类原本由 `MarkdownScrollViewer` 挂在文档根上；绕开它必须自己给文档根补上，否则标题 / 列表 / 引用 / 表格 / 代码块的样式全部不生效，整篇渲染成一团平文本（聊天气泡与简报弹窗曾经就是这样）。
- **设计令牌覆盖层**在 `Resources/Styles/MarkdownStyles.axaml`（前景、标题字号与字重、列表缩进、表格与代码块的边框 / 底色），由 `RichTextBlock` 在库样式集**之后**加到控件级 `Styles`（同一属性后加入的样式优先）。**不要放进 `App.axaml`**：应用级的后代选择器会让全库每个 `TextBlock` 都去评估祖先链。
- 改了覆盖层或渲染结构，用无头渲染看一遍亮 / 暗两套主题（标题层级、表格斑马纹、行内代码、引用条），暗色下标题只能靠字号区分层级（`WeightTitle` 的 Medium 在中文字体下渲染为 Regular，是全局限制）。

### 资产详情页（AssetPageView）

- **价格取位按市场**：`PriceFormatter.Format(price, market, code)` —— A 股股票固定 2 位、基金 / ETF（`StockSymbolConverter.IsFundOrEtf`）固定 3 位（按最小报价单位）；虚拟币价格跨度极大，按量级取位（≥1000 取 2 位，≥1 取 4 位，<1 取 6 位，保护低价币）。页头价格、涨跌额、价差、盘口档位统一经 `AssetPageViewModel.FormatPrice`，不要在各处自己按量级格式化，否则 A 股涨跌额会显示成 `0.470000`。
- **盘口数量 / 金额列定宽 76px**：按 `MarketValueFormatter` 的最长真实输出取值（`9,999.99万` ≈ 70px，`9,900.00` ≈ 58px，12px 等宽字）再留 6px 间隙。右栏内容宽约 271px（被占比条 `ProgressBar` 的主题 `MinWidth=200` 撑开），扣掉 `34 + 76 + 76` 后价格列约 85px，够放 `67,234.50`。**改列宽前用真实格式化路径渲染**（无头渲染里调 `ApplyOrderBook`，喂真实量级的数量与金额），不要手写假文案验证：手写的短文案正是之前没发现数量与价格挤在一起、金额被卡片边缘截断的原因。
- **行情数据是两列 `UniformGrid`**，`BuildMarketMetrics` 直接略过无值项、不留占位，网格里没有空洞；骨架与数据同构（2 × 2）。不要再用固定宽度单元格 + `WrapPanel`：右栏内容宽最多 320px，放不下两个 `148 + 16`，会被折成单列并在右侧留出一半空白。
- **K 线图例的涨跌色用 `BullishBrush` / `BearishBrush`**，与 `kline_chart.html` 的 `upColor` / `downColor` 同源；不要用 `ErrorBrush` / `SuccessBrush`（语义色，且随主题变色，暗色下的红会与蜡烛颜色不一致）。

### 报告导出（Markdown / 分享图 / PDF）

- Markdown 导出走 App.Services 的 `MarkdownReportExporter`（纯文本，无渲染依赖）。
- 分享图/PDF 走本项目的 `ReportShareCardRenderer`：固定 1080×1350 画布离屏渲染 `ReportShareCardView`（Avalonia 视觉树，中文与应用字体由宿主保证），PDF 由渲染位图经 SkiaSharp `SKDocument` 封装单页——**不引入第二套排版**，分享图与 PDF 永远视觉一致。
- `RenderTargetBitmap` 要求 UI 线程：导出命令只在 VM（UI 线程）同步调用渲染，不要 `Task.Run`。
- PDF 写入前先释放 `SaveFilePickerAsync` 返回的流，再以本地路径交给 SkiaSharp，避免文件占用；本地路径取 `IStorageFile.TryGetLocalPath()`。
- 分享卡数据模型是 `ReportShareCardData`（不可变快照）：只带展示必需字段，不要把 `MarketAnalysisReport` 直接绑到离屏视图上。

---

## 边界约定

- `MarketAssistant.App` 不再承载 Agent Tool、Workflow、RAG、交易引擎等运行时代码。
- 新增 Agent/Tool/Workflow/业务服务时，放到 `MarketAssistant.App.Services`；新增文档解析、向量化、检索、重排等 RAG 基础能力时，放到 `MarketAssistant.Rag`；本项目只保留 UI 相关适配。
- ViewModel 禁止直接注入 `MarketAssistant.DataProviders` 的具体实现类（如 `BinanceMarketDataService`、`BinanceWebSocketService`）；行情/实时数据应经 `MarketAssistant.App.Services` 编排层暴露的抽象访问，避免多市场抽象泄漏到 UI 层。
- `skills/` 作为内容文件随 App 输出，由运行时从输出目录加载；不要在 UI 层复制第二套 Skill 加载逻辑。
- 版本号定义在 `.csproj` 的 `<Version>` 属性中，运行时通过 `AppInfo.Version` 获取。

---

## DI 注册

- 入口：`Program.ConfigureServices()`。
- UI 注册入口：`Services/ServiceCollectionExtensions.cs` 中的 `AddApplicationServices()` 和 `AddViewModels()`。
- `AddApplicationServices()` 会先调用 `AddBusinessServices()`，后者定义在 `MarketAssistant.App.Services/Services/ServiceCollectionExtensions.cs`。
- 新增 UI 服务在本项目注册；新增业务/运行时服务在 `MarketAssistant.App.Services` 注册。

---

## 安全

- API 密钥（Binance、AI 模型）通过 `IUserSettingService` 管理，持久化在用户本地目录，**禁止提交到仓库**。
- 涉及 Binance 等交易所签名逻辑的代码修改时，需确保 HMAC 密钥不被日志记录。

---

## 配置文件

- `config/prompts/*.yaml`：分析师提示词配置，每个分析师一个独立文件（如 `FundamentalAnalyst.yaml`），含 `name`、`displayName`、`temperature`、`topP`、`topK`、`instructions` 字段，运行时热加载。

---

## 构建与运行

```bash
# 构建
dotnet build src/MarketAssistant.App/MarketAssistant.App.csproj -c Debug

# 运行
dotnet run --project src/MarketAssistant.App/MarketAssistant.App.csproj -c Debug
```
