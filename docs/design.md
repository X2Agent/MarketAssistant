# MarketAssistant 界面设计系统 v3.0

> **版本**：3.0
> **状态**：当前唯一生效的设计规范（取代 v1.2 及更早的设计文档）
> **实现载体**：Avalonia 12.1.1 / C# XAML（本项目为桌面客户端，不使用 React/Vue/Tailwind；
> 文中所有"组件"均以 Avalonia 样式类 + 自定义控件实现，等价于 Web 侧的组件契约）
> **令牌唯一事实源**：`src/MarketAssistant.App/Resources/Styles/` 下的 `Colors.axaml`、
> `Spacing.axaml`、`Elevation.axaml`、`Motion.axaml`
>
> 本文档描述**规则**，令牌文件描述**取值**。两者冲突时以令牌文件为准，并回头修正本文档。

---

## 一、设计定位

MarketAssistant 是面向专业投资者的 AI 原生市场工作站，横跨 A 股与虚拟币两个市场。
界面服务于**长时间盯盘**与**高频数据刷新**，因此设计目标是：

1. **信息优先**：一切装饰让位于数据可读性。没有渐变、没有发光、没有毛玻璃、没有描边。
2. **焦点唯一**：任何一屏只有一个强调色焦点，用户永远知道下一步该点哪里。
3. **秩序稳定**：布局、字号、间距、动效节奏全库一致，不因页面而异。
4. **克制的质感**：高级感来自留白、色阶层次与排版精度，而不是阴影堆叠或动效炫技。

### 四条不可协商的底线

| # | 规则 | 违反示例 |
|---|---|---|
| 1 | **只有一个交互强调色** `#0066FF` | 第二个蓝色、紫色/橙色功能图标、彩色渐变 |
| 2 | **不画边框**，靠表面色阶（1%–3%）与阴影分层 | `BorderThickness="1"` + `BorderBrush` |
| 3 | **全局只有五档字号**（32/24/16/14/12） | 出现 11px / 13px / 18px / 22px / 28px |
| 4 | **等宽字体只给数字** | 标的代码、按钮文字、正文用等宽 |

---

## 二、色彩系统

### 2.1 唯一强调色

| 令牌 | 值 | 用途 |
|---|---|---|
| `AccentColor` / `AccentBrush` | `#0066FF` | 主按钮、选中态、焦点环、进度、链接 |
| `AccentHoverColor` / `AccentHoverBrush` | `#0052CC` | 强调色 hover（加深，不提亮） |
| `AccentPressedColor` / `AccentPressedBrush` | `#0040A3` | 强调色 pressed |
| `AccentSubtleBrush` | `#0F0066FF` | 强调浅底（功能按钮、评级徽章） |
| `SelectedBackgroundBrush` | 浅 `#1A0066FF` / 深 `#290066FF` | **仅限**导航项、列表项选中、行内小面积选中块 |

**`SelectedBackgroundBrush` 不是"浅色区块底"**：它表达"这一项当前被选中"，
只能用在可选中元素的**局部**。拿它铺满整张大卡片会让页面多出第二个强调色焦点，
输入框（`SunkenBackgroundBrush`）在它上面也会失去凹陷层次。区块底色用
`CardBackgroundBrush` / `SurfaceVariantBrush`。

**纪律**：一个视图内原则上只出现一个 `btn-primary`。次要按钮一律用中性色阶，不得使用第二个彩色。

### 2.2 中性色阶（浅色）

层次完全由相邻层的极小色差产生，这是"无边框也能分层"的原理。

| 令牌 | 值 | 语义 |
|---|---|---|
| `PageBackgroundBrush` | `#F5F7FA` | 页面底色（最底层） |
| `CardBackgroundBrush` | `#FFFFFF` | 卡片表面（浮于页面之上） |
| `SunkenBackgroundBrush` | `#EEF0F3` | 凹陷面（输入框未聚焦、代码块） |
| `SurfaceVariantBrush` | `#F0F2F5` | 次级表面（瓦片、标签底色） |
| `HoverBackgroundBrush` | `#F0F2F5` | hover 态 |
| `PressedBackgroundBrush` | `#E6E9EE` | pressed 态 |
| `DividerBrush` | `#E4E7ED` | 分隔线（仅表格行） |

### 2.3 中性色阶（暗色）

暗色**不使用蓝调**。蓝底会让中性面发脏，也会与强调色相互污染。

| 令牌 | 值 | 语义 |
|---|---|---|
| `PageBackgroundBrush` | `#0D0D0D` | 页面底色 |
| `SunkenBackgroundBrush` | `#101010` | 凹陷面 |
| `CardBackgroundBrush` | `#161616` | 卡片表面 |
| `SurfaceVariantBrush` | `#1C1C1C` | 次级表面 |
| `HoverBackgroundBrush` | `#1F1F1F` | hover |
| `PressedBackgroundBrush` | `#262626` | pressed |
| `DividerBrush` | `#262626` | 分隔 |

### 2.4 文字

| 令牌 | 浅色 | 暗色 | 用途 |
|---|---|---|---|
| `TextPrimaryBrush` | `#14161A` | `#EDEDED` | 标题与正文（唯一允许接近纯黑/纯白的地方） |
| `TextSecondaryBrush` | `#5C6470` | `#A0A0A0` | 辅助说明、次要字段 |
| `TextDisabledBrush` | `#8A93A0` | `#6E6E6E` | 禁用、占位 |
| `TextOnEmphasisBrush` | `#FFFFFF` | `#FFFFFF` | 强调实底上的文字 |

**暗色不取纯白**：`#EDEDED` 而非 `#FFFFFF`。纯白在深底上会产生"溢出"观感，显得发粗刺眼。

### 2.5 语义色（只表达数据语义，禁止装饰）

| 令牌 | 值 | 用途 |
|---|---|---|
| `BullishBrush` | `#E5484D` | 上涨（中国市场红涨） |
| `BearishBrush` | `#16A34A` | 下跌 |
| `NeutralBrush` | `#8A93A0` | 平盘 |
| `SuccessBrush` | `#16A34A` | 成功提示 |
| `WarningBrush` | `#D97706` | 警告 |
| `ErrorBrush` | `#DC2626` | 错误、危险 |

**纪律**：语义色不得用于图标底色、装饰块、品牌表达。涨跌色只出现在数值本身与涨跌标签上。
状态标签（一次性 / 已触发 / 紧急）用 12% 语义底 + 同色字，见 7.5；不要用语义色实底。

涨跌标签使用 12% 透明度色块 + 同色文字（`BullishTagBackgroundBrush` 等），不使用描边。

### 2.6 v2 令牌键已删除

v2 的 `Primary` / `PrimaryBrush` / `PrimaryLightBrush` / `PrimaryDarkBrush` / `Info` / `InfoBrush`
等键**已全部删除**，没有别名兼容层。新代码只用 v3 键：`AccentBrush`、`AccentHoverBrush`、
`AccentPressedBrush`、`AccentSubtleBrush`。

> 这条曾经写成"旧键全部保留"，结果 `LoadingDots` 里按字符串取 `PrimaryBrush` 失效、
> 加载点阵整个不可见。**不要为省事再补一批指向 `AccentBrush` 的别名**——那会让
> 两种写法长期并存，且 XAML 的 `FindResource` 语义是"找不到就返回 UnsetValue"，静默失败。

---

## 三、排版系统

### 3.1 五档字号（无第六档）

> 为什么是五档而不是四档：`BodySm`(14) 不是新造的一档语义，而是 `Body`(16) 在**高密度场景**
> （表格、表单、卡片、按钮）的降级形态。信息密集是金融终端的常态，只有 4 档就必须在
> "正文过大挤掉行数"和"正文过小读不清"之间二选一。层级仍靠**字重 + 颜色**拉开，
> 不靠继续细分字号。

| 档位 | 令牌 | 字号 / 行高 | 字重令牌 | 用途 |
|---|---|---|---|---|
| **大数字** | `DisplayFontSize` | 32 / 40 | `WeightNumber` | 资产现价、余额、进度百分比（等宽） |
| **标题** | `TitleFontSize` | 24 / 32 | `WeightTitle` | 页面标题、弹窗标题、区块主标题 |
| **正文** | `BodyFontSize` | 16 / 24 | `WeightBody` | 阅读型内容：聊天、报告、说明 |
| **正文小** | `BodySmFontSize` | 14 / 21 | `WeightBody` | **主力档**：表格、卡片、表单、按钮 |
| **辅助** | `CaptionFontSize` | 12 / 18 | `WeightBody` | 时间戳、单位、表头、代码、标签 |

**唯一例外**：报告分享卡（`ReportShareCardView`，固定 1080×1350 离屏画布）使用
48 / 36 / 96 等放大字号。它是导出到外部尺寸的位图，不参与应用内排版权重，
故不遵守应用内五档。除该文件外不得出现第六档。

卡片标题使用 `BodyFontSize` + `WeightTitle`（`card-title` 类），**靠字重而非新字号**拉开层级。

### 3.2 字重主题化（暗色自动降一档）

字重不是硬编码，而是主题资源。切主题时 `DynamicResource` 会自动重算。

| 令牌 | 浅色 | 暗色 | 用途 |
|---|---|---|---|
| `WeightTitle` | `SemiBold` (600) | `Medium` (500) | 标题、卡片标题、区块标题 |
| `WeightEmphasis` | `Medium` (500) | `Normal` (400) | 强调正文、表单标签、按钮文字 |
| `WeightBody` | `Normal` (400) | `Normal` (400) | 正文 |
| `WeightNumber` | `SemiBold` (600) | `Medium` (500) | 数字 |

**这是硬性要求**：`FontWeight="Bold"` / `"SemiBold"` 等字面量已全库清除。
新增代码必须使用 `{DynamicResource WeightTitle}` 形式，否则暗色模式会出现"白字发粗刺眼"。

### 3.3 字体族

```
UiFontFamily   = Inter, Microsoft YaHei UI, PingFang SC, Noto Sans SC, Source Han Sans SC, sans-serif
DataFontFamily = JetBrains Mono, SF Mono, Consolas, Cascadia Mono, Menlo, monospace
```

- `Inter` 无中文字形，**必须**保留中文回退链，否则中文会落到不确定的系统兜底字体，导致字面与字重不一致。
- `UiFontFamily` 已在 `App.axaml` 设为 Window / UserControl / TextBlock / TextBox / Button 的默认值。
- **`DataFontFamily` 只给数字**：价格、涨跌、成交量、计数。标的代码属于文本，用 `UiFontFamily`。
- **数值锁宽的两层保障**：`DataFontFamily` 提供等宽字体族；同时 `App.axaml` 给全局
  `TextBlock` 设了 `FontFeatures="tnum"`（表格数字）。原因：`JetBrains Mono` 未随包发布，
  三平台实际回退到 Consolas / SF Mono / DejaVu，字宽各不相同；`tnum` 保证等宽族缺失时
  仍逐位对齐，刷新时数字不会左右跳动。`tnum` 只作用于数字字形，不影响中文。
  需要固定列宽的数字（价格、涨跌、持仓量）**不要**靠 `Width` 猜，交给等宽 + `tnum`。

### 3.4 禁止的排版手法

- 全大写标签（`ToUpper`、`TextTransform`）——中文无大小写，只对英文有害。
- 斜体（`FontStyle="Italic"`）。
- 用字距（`LetterSpacing`）制造"高级感"。
- 正文里混用三种以上字重。

---

## 四、间距、圆角与栅格

### 4.1 间距阶梯

`4 / 8 / 12 / 16 / 20 / 24`，全部为 4 的倍数。

| 场景 | 令牌 | 值 |
|---|---|---|
| 页面内边距 | `PagePadding` | 24 |
| 紧凑页面内边距 | `PagePaddingCompact` | 16 |
| 卡片内边距 | `CardPadding` | 20 |
| 普通卡片内边距 | `DefaultCardPadding` | 16 |
| 紧凑卡片内边距 | `CompactCardPadding` | 12 |
| 区块间距 | `SectionSpacing` | 24 |
| 栅格 gutter | `GridGutter` | 16 |
| 列表行内边距 | `ListItemPadding` | 12,10 |
| 卡片内嵌行内边距 | `SmallCardPadding` | 8 |
| 手风琴内容内边距 | `ExpanderContentPadding` | 20,16,20,20 |

`ExpanderContentPadding` 是**全局令牌**（`Spacing.axaml`）。手风琴壳（`Expander.section-card`）
本身不留边距，页面引用该令牌给内容留白；**不要在页面 `Resources` 里重复定义**。

### 4.2 圆角

| 令牌 | 值 | 用途 |
|---|---|---|
| `CardCornerRadius` | 12 | 卡片、弹窗、导航项 |
| `SmallCornerRadius` | 8 | 按钮、输入框、分段器 |
| `TinyCornerRadius` | 6 | 标签、小图标按钮 |

### 4.3 栅格与布局

- 桌面基准 **1440×900**，平板 **768×1024**，窄窗下限 **720×600**（`MainWindowMinWidth/MinHeight`）。
- 内容区按 **12 列**组织，gutter 16。两栏对比常用 `7:5`（7+5=12）或 `8:4`。
- 侧边导航 **240px**（图标 20 + 标签）。**无顶栏**（v3 移除）：页面标题归属页面自身，
  二级页面的返回入口是内容区顶部的**布局内窄条**（不浮于内容之上），行情条归属首页头部。

**栅格基元 `PageGrid`**（`Views/Controls/PageGrid.cs`）：列已预置为 12 个等宽星号列，
子元素用 `Grid.Column` + `Grid.ColumnSpan` 表达跨度，无需手写 `ColumnDefinitions`：

```xml
<controls:PageGrid ColumnSpacing="{StaticResource GridGutter}">
    <controls:CardView Grid.Column="0" Grid.ColumnSpan="7"/>
    <controls:CardView Grid.Column="7" Grid.ColumnSpan="5"/>
</controls:PageGrid>
```

列间距由 `Grid.ColumnSpacing` 直接给出（只插在相邻列之间，首列贴左、末列贴右）。
**不要再给子元素加左右 Margin**，否则会双倍间距。

> 早期实现用"改写子元素 Margin"模拟 gutter，会连子元素自己写的上下边距一起覆盖掉，
> 已改回原生的 `ColumnSpacing`。

**固定宽度侧栏不走 `PageGrid`**：资产详情页右栏是 260–360px 的固定宽面板
（`MinWidth`/`MaxWidth`），不是栅格比例。用 12 列跨度表达会在 720px 窄窗下把它压到
200px、把盘口表格挤坏，所以那一栏继续用 `ColumnDefinitions="*,Auto"`。判断依据是
「这一栏的宽度由内容需求决定，还是由栅格比例决定」——前者用 `Auto` + `Min/MaxWidth`，
后者才用 `PageGrid` 跨度。

### 4.4 响应式（桌面应用语境）

本项目是桌面客户端，不追求 375px 手机布局，而是做**窄窗自适应**：

| 断点 | 行为 |
|---|---|
| ≥ 1024px | 240px 文字导航 + 多列栅格（无顶栏） |
| < 1024px | 导航收起为 **72px 图标轨**（标签与分组标题隐藏，图标居中），内容区让出空间 |

实现方式：`MainWindow` 监听 `SizeChanged`，在 `ShellGrid` 上切换 `compact` 类，
由样式规则完成收起，不逐个遍历可视树。断点与两档宽度读 `CompactWindowBreakpoint` /
`NavRailWidth` / `NavRailCollapsedWidth` 令牌，代码后置不写字面量。

---

## 五、层次与阴影

层次**首先靠表面色阶**，阴影是第二手段。

### 5.1 阴影纪律

| 规则 | 说明 |
|---|---|
| 卡片默认无阴影 | 靠 `#FFFFFF` 与 `#F5F7FA` 的 2%–3% 色差分层 |
| 只有真正浮于内容之上才给阴影 | 下拉菜单、Popup、模态窗口、悬浮通知 |
| 禁止发光 | 不允许彩色光晕（旧版用户气泡的品牌色光晕已移除） |
| 禁止毛玻璃 | 不使用背景模糊 |
| 重阴影 = 大范围 + 低透明度 | 不是高透明度 |

### 5.2 阴影层级（`Elevation.axaml`）

只有三层，**没有"卡片阴影"**：

| 令牌 | 用途 |
|---|---|
| `ShadowOverlay` | 下拉菜单 / Popup / 自动补全浮层 |
| `ShadowModal` | 模态窗口 / 悬浮通知（唯一允许"重阴影"处） |
| `AccentRing` | 卡片式单选的选中环（与 `FocusRing` 同几何，语义是"当前选中"） |

旧版的 `ShadowCard` / `ShadowCardHover` / `ShadowDragging` 已删除：卡片靠色阶分层、
不给阴影，当前版本也没有拖拽排序场景。

### 5.3 焦点双环

输入框与可聚焦元素**不画亮线、不改边框颜色**，用双环阴影表达焦点：

| 令牌 | 值 | 语义 |
|---|---|---|
| `FocusRing` | `0 0 0 1 #0066FF, 0 0 0 4 #290066FF` | 内 1px 实色贴边 + 外 3px 半透明扩散 |

注意：`BoxShadow` 只存在于 `Border` / `ContentPresenter` 上，因此聚焦环必须打在模板内部
`Border#PART_BorderElement` 上，不能直接写 `TextBox.BoxShadow`。

---

## 六、动效系统

动效分四种性格，**不可混用**。

### 6.1 性格 A —— 响应（交互反馈）

- 时长 **150ms**，`CubicEaseOut`
- 只改**颜色与不透明度**，不改尺寸
- **按钮 hover 仅加深色阶与阴影，禁止放大、禁止位移**
- 焦点环 150ms 淡入

### 6.2 性格 B —— 入场（内容出现）

- 时长 **200–320ms**，`CubicEaseOut`
- 只做 `Opacity` 0→1 + 极小位移（≤4px）
- 禁止整块缩放；列表 stagger 不超过 6 项
- 骨架呼吸：1.6s，Opacacity 0.55 ↔ 0.95（低对比，无扫光）

### 6.3 性格 C —— 性格（表达情绪）

- 只用于**用户主动触发且结果可逆**的动作：点赞、复制、收藏
- `BackEaseOut`，过冲 10%–20%，时长 **240–320ms**
- 退场**不再回弹**，只做 180ms 淡出

### 6.4 性格 D —— 物理（形体与动量）

只作用于**形体与粒子**，不与性格 A 的色阶过渡叠加在同一元素上：

| 场景 | 行为 | 参数 |
|---|---|---|
| 开关圆点位移 | 位置插值，**不做位置过冲**（否则跑出 40px 轨道） | 220ms / `CubicEaseOut` |
| 开关圆点拉伸 | 位移途中拉成胶囊（横向 1.25、纵向 0.90） | 90ms / `CubicEaseOut` |
| 开关圆点回落 | 落回圆形，带轻微回弹 | 200ms / `BackEaseOut`（≈12% 过冲） |
| 收藏烟花 | 12 粒光点径向扩散 + 阻尼 + 淡出 | 520ms |

**边界**：性格 D **禁止回流到按钮 hover/press**——那是性格 A 的地盘，在按钮上做缩放会让
整条工具栏抖动。也禁止用于"页面入场"（性格 B 的地盘）与"退场"（退场一律只淡出）。

实现载体：`Views/Controls/KnobPill`（开关圆点）、`Views/Controls/CelebrationBurst`（烟花）。
两者都只在**真实状态变化**时播放：首次绑定（`null → true`）不播，
否则列表一次性创建的多个开关会同时抽动一下，读起来像页面在错误抖动。

### 6.5 拖拽

**0ms 瞬时到位**，不做补间。当前版本无拖拽排序功能，故**没有拖拽阴影令牌**；
导航与页签切换采用"瞬时切换"，不播放滑动补间。若未来加入拖拽排序，再补 `ShadowDragging`。

### 6.6 关键动效参数

| 场景 | 参数 |
|---|---|
| 普通响应 | 150ms / CubicEaseOut |
| 内容入场 | 200ms / CubicEaseOut |
| 面板展开 | 320ms / CubicEaseOut |
| 骨架呼吸 | 1600ms / 无限循环 / Opacity 0.55↔0.95 |
| 复制反馈入场 | 180ms / BackEaseOut（振幅 0.15 ≈ 12% 过冲） |
| 复制反馈停留 | **2500ms** |
| 复制反馈退场 | 180ms / CubicEaseIn |
| 流式单字模糊消退 | 220ms / CubicEaseOut，`BlurEffect.Radius` 4→0 |
| 流式相邻字错开 | **35ms** |
| 开关圆点位移 | 220ms / CubicEaseOut（无位置过冲） |
| 开关圆点拉伸 / 回落 | 90ms CubicEaseOut / 200ms BackEaseOut |
| 收藏烟花 | 520ms / 12 粒 |

### 6.7 流式打字"模糊 → 清晰"

实现载体：`Views/Controls/StreamingTextReveal`。

- **按单元切分**，不按字符：CJK 逐字、拉丁按词。拉丁若按字符切会拆散单词，失去可读性。
- 每个单元独立 `BlurEffect.Radius 4→0` + `Opacity 0.35→1`，相邻错开 35ms。
- **性能边界**：只有"当前段落"参与动画，已完成的段落合成为静态 `TextBlock`。
  追加时**只对本次新增的单元建块并播放一次揭示**，已揭示的单元保留原块（仅更新文本），
  否则每隔一个 token 就把整段重新模糊一遍（O(n²) 且持续抖动）。
  同时最多 24 个单元处于过渡中，超出直接清晰显示；单个单元的 `BlurEffect` 在动画结束后立刻摘下。
- 错峰计时从 0 起算（按本批新增单元的序号），不按整段绝对下标，
  否则长段落末尾的新字要等上几秒才开始变清晰。
- 文字立即可见（仅略模糊），因此不拖慢阅读，也不产生布局跳动。
- 非增量变化（换消息、清空、内容回退）直接整体替换，不播放动效。

---

## 七、组件规范

组件以 Avalonia **样式类**（`Classes="..."`）提供，等价于 Web 的组件 props。

### 7.1 Button 按钮

**变体**

| 类名 | 视觉 | 用途 |
|---|---|---|
| `btn-primary` | 强调色实底 + 白字 | 页面唯一主操作 |
| `btn-secondary` | `SurfaceVariantBrush` 底 + 主文字 | 次要操作（无描边） |
| `btn-ghost` | 透明底，hover 才出底色 | 低优先级、工具栏 |
| `btn-danger` | 危险浅语义底 + 语义字 | 破坏性操作（非实心红，避免抢焦点） |
| `btn-icon` / `btn-icon-sm` | 纯图标，40 / 32 点击区 | 图标操作 |

**尺寸**

| 类名 | 高度 | 字号 | 内边距 |
|---|---|---|---|
| `btn-sm` | 30 | 12 | 12,0 |
| 默认 | 36 | 14 | 16,0 |

**没有 `btn-lg`**：更大的按钮用默认尺寸 + 更大的 `Padding`，不再另设变体——
多一个尺寸变体只会让"这个按钮为什么比旁边高"变成需要解释的事。

**状态**

| 状态 | 视觉 |
|---|---|
| hover | 色阶加深一档（primary：`#0066FF` → `#0052CC`）。**不放大、不位移、不加亮线** |
| pressed | 再加深一档 |
| focus-visible | `FocusRing` 双环 |
| disabled | `Opacity` 0.4 |

**过渡**：`Background` / `Foreground` 150ms，`BoxShadows` 150ms。

### 7.2 TextBox 输入框

| 状态 | 视觉 |
|---|---|
| 默认 | `SunkenBackgroundBrush` 底，无边框，圆角 8，高度 40 |
| hover | 底色提到 `SurfaceVariantBrush` |
| **focus** | 底变 `CardBackgroundBrush` + `FocusRing` 双环（1px 实色 + 3px 半透明扩散） |
| disabled | `Opacity` 0.45 |

`CaretBrush` = 强调色；`SelectionBrush` = `SelectedBackgroundBrush`。
模板内 `Border#PART_BorderElement` 的 `BorderThickness` 必须归零，否则与双环叠加成双层描边。

### 7.3 Card 卡片

| 类名 | 视觉 |
|---|---|
| `card` | 白底（暗色 `#161616`）、圆角 12、内边距 20、**无边框无阴影** |
| `hot-stock-card` | `SurfaceVariantBrush` 底，hover 加深 |
| `mode-card` | 可选卡片，选中态 `SelectedBackgroundBrush` + `AccentRing` 阴影环（不是 1px 描边） |
| `rightSection` | 侧栏区块，同 `card` |
| 页面内自定义（`fav-card` / `q-card` / `rst-card` / `serverCard`） | 各页面自己声明；hover 一律加深色阶、选中用 `AccentRing` |

**没有通用 `card-interactive`**：它会把 `Cursor=Hand` 塞给任何卡片，让只读卡片看起来可点。
可点击性由具体卡片的样式类表达。

**`CardView` 控件**：字符串标题 → 16px `WeightTitle`；视图标题走 `Header` 属性。
v3 移除了标题与内容之间的分隔线，改由标题的 12px 底部外边距（`BottomMargin`）建立间距。

### 7.4 列表与表格

| 类名 | 视觉 |
|---|---|
| `nav-list ListBoxItem` | 高度 40，圆角 8，选中 `SelectedBackgroundBrush` + 强调色标签 |

`ListBoxItem` 只有基础外观；具体行样式由页面声明（如 `serverCard`、`recent-chip`）。
通用的 `data-row` / `stock-item` 变体已下线——仓库内零使用，留着只会让人以为"表格行有统一外观"。

`DataGrid`：无外框、无竖线，仅水平极浅分隔；表头 12px `WeightEmphasis` + 次级色。

### 7.5 标签 / 徽章

| 类名 | 视觉 |
|---|---|
| 涨跌标签 | 12% 语义色底 + 同色 `WeightNumber` 文字，圆角 6，内边距 6,2，无边框 |
| 状态标签（一次性 / 已触发 / 紧急） | 12% 语义色底（`WarningTagBackgroundBrush` / `DangerTagBackgroundBrush`）+ 同色 `Caption` 文字 |
| 代码徽章 | `SurfaceVariantBrush` 底 + 次级文字（非实心强调色） |

**状态标签一律不用语义色实底**：实心 `WarningBrush` / `ErrorBrush` 底 + 白字对比度不足 AA，
且会在页面上凭空造出第二个高饱和焦点，与「一屏只有一个强调色」冲突。

语义面板同理：**没有通用 `panel-*` 类**，直接给 `Background` 绑定对应的
`XxxPanelBackgroundBrush` 令牌，再配圆角与内边距。提示条的文字必须用配对的 `XxxPanelTextBrush`，
不要用裸 `WarningBrush` / `ErrorBrush` 配 `SurfaceVariantBrush` 底（跨主题对比度会翻车）。

### 7.6 骨架屏

Avalonia **没有内置骨架控件**（官方控件库无 Skeleton / Shimmer；第三方如 Nova 的 `Shimmer`、
Huskui 的 `SkeletonContainer` 都要么依赖渐变扫光、要么拖进整套设计系统）。本项目自建**三个控件**，
各司其职，**不要互相替代**：

| 层 | 控件 | 职责 | 数量语义 |
|---|---|---|---|
| 组 | `SkeletonList` | 重复 N 个 | N |
| 卡 | `SkeletonCard` | 一个骨架的壳 + 内容 | 1 |
| 行 | `SkeletonLines` | 一段等高灰条（一种字号） | 一段内的多行 |

> **为什么重复必须是独立控件**：一个 `SkeletonCard` 实例只能挂在一个地方。若把"重复"
> 做成它的属性，就必须额外发明 `ContentTemplate` 才能复制内容，并为"单卡 / 多卡"写特判
> 分支——这是设计缺陷的信号。用 `ItemsControl.ItemTemplate` 天然为每项创建独立实例。

核心原则：**一个 `SkeletonLines` 只表达一段等高灰条**（一种字号）。真实卡片里
"代码 12 / 名称 14 / 价格 24"是多段、字号各不相同，要纵向放多个 `SkeletonLines`。

> 这条是踩坑换来的：曾用 `LineWidths="52,40|120|110"` 把多段压进一个字符串属性，
> 结果四行全部按同一字号绘制（与真实内容对不上），组间距逻辑也写错导致完全无间距。
> 段与段的区别就是字号，编码进一个字段必然丢掉它。

#### 用法一：单卡单段（最简单）

属性驱动，不需要写子元素：

```xml
<controls:SkeletonCard Variant="Row" Row="BodySm" LineWidths="200,*"/>
```

#### 用法二：单卡多段（字号不同）

把多个 `SkeletonLines` 写进 `Content`：

```xml
<!-- 自选卡：代码 12（两行）/ 名称 14 / 价格 24 -->
<controls:SkeletonCard Variant="Card" Width="290">
    <StackPanel Spacing="10">
        <controls:SkeletonLines Row="Caption" LineWidths="52,40" Spacing="2"/>
        <controls:SkeletonLines Row="BodySm" LineWidths="120"/>
        <controls:SkeletonLines Row="Title" LineWidths="110"/>
    </StackPanel>
</controls:SkeletonCard>
```

#### 用法三：列表首屏（重复 N 条）

外层套 `SkeletonList`，`ItemTemplate` 里放一个 `SkeletonCard`：

```xml
<controls:SkeletonList Count="5"
                       Spacing="{StaticResource SmallSpacing}"
                       IsVisible="{Binding IsInitialLoading}">
    <controls:SkeletonList.ItemTemplate>
        <DataTemplate>
            <controls:SkeletonCard Variant="Row">
                <StackPanel Spacing="{StaticResource TinySpacing}">
                    <controls:SkeletonLines Row="BodySm" LineWidths="200"/>
                    <controls:SkeletonLines Row="Body" LineWidths="*"/>
                    <controls:SkeletonLines Row="Caption" LineWidths="180"/>
                </StackPanel>
            </controls:SkeletonCard>
        </DataTemplate>
    </controls:SkeletonList.ItemTemplate>
</controls:SkeletonList>
```

横排（如"最近查看"的 chip）用 `Orientation="Horizontal"`。

#### 用法四：多列网格

`SkeletonList` 默认单列；两列瓦片、四张指标卡显式给 `ItemsPanel`：

```xml
<controls:SkeletonList Count="6" IsVisible="{Binding HotAssets.IsInitialLoading}">
    <controls:SkeletonList.ItemsPanel>
        <ItemsPanelTemplate><UniformGrid Columns="2"/></ItemsPanelTemplate>
    </controls:SkeletonList.ItemsPanel>
    <controls:SkeletonList.ItemTemplate>
        <DataTemplate>
            <controls:SkeletonCard Variant="Tile" Margin="{StaticResource SmallMargin}">
                <StackPanel Spacing="4">
                    <controls:SkeletonLines Row="BodySm" LineWidths="96"/>
                    <controls:SkeletonLines Row="Caption" LineWidths="64"/>
                </StackPanel>
            </controls:SkeletonCard>
        </DataTemplate>
    </controls:SkeletonList.ItemTemplate>
</controls:SkeletonList>
```

#### 属性速查

**`SkeletonList`**

| 属性 | 说明 |
|---|---|
| `Count` | 重复次数 |
| `Spacing` | 条目间距（仅默认面板生效） |
| `Orientation` | 默认面板方向，`Horizontal` 用于一排 chip |
| `ItemsPanel` | 显式覆盖面板（多列网格用 `UniformGrid`） |

**`SkeletonCard`**

| 属性 | 说明 |
|---|---|
| `Variant` | `Card`(内边距20/圆角12) / `Tile`(16/12) / `Row`(16/8) / `Bare`(无表面)，**必须与真实卡片一致** |
| `Row` / `LineCount` / `LineWidths` | 单段内容时的灰条参数 |
| `LineSpacing` | 单段内容时的行距，默认 6 |
| `LinesOrientation` | 单段内容时的灰条方向 |
| `Content` | 多段内容（多个 `SkeletonLines`），此时上面的行属性被忽略 |

**`SkeletonLines`**

| 属性 | 说明 |
|---|---|
| `Row` | 行高档位 `Caption`(18) / `BodySm`(21) / `Body`(24) / `Title`(32)，高度取 `Spacing.axaml` 行高令牌 |
| `LineWidths` | 逗号分隔行宽，`*` 撑满；**给了此项时行数以它为准** |
| `LineCount` | 不写 `LineWidths` 时的行数 |
| `Spacing` | 段内行间距，默认 6 |
| `RowHeight` | 覆盖行高，用于非文字占位（盘口档位 20、周期按钮 38） |
| `LineWidth` + `Orientation="Horizontal"` | 横向一排等宽小块 |

#### 约束

- 骨架只服务首屏（`IsInitialLoading`）；刷新（已有内容）不显示骨架。
- 呼吸 1.6s，Opacity 0.55↔0.95，只用色阶、不用渐变扫光（暗色下扫光像"发光"）。
- 行宽刻意参差：读起来是"内容即将出现"，等宽等高的一摞灰条则像"表格在加载"。
- **禁止用 `MinHeight` 猜高度**。`skeleton-text/title/row/card/panel` 已全部下线。
- **不要复用真实卡片的样式类**（`news-card` / `fav-card`）当骨架容器：会带上 `Cursor=Hand`
  与 hover 高亮，让占位看起来可点。用 `Variant` 代替。
- **不要手写 `<Border>` 充当骨架专用的卡片壳**，也不要复用真实卡片的样式类
  （`news-card` / `fav-card`，会带上 `Cursor=Hand` 与 hover 高亮）。用 `SkeletonCard.Variant`。
- **但骨架在真实卡片内部时不要包 `SkeletonCard`**：例如 `AssetPageView` 右栏的
  `<Border Classes="rightSection">` 本身就是真实卡片（数据到达后仍在），骨架只是填它的内容区。
  再包一层会变成"真实卡里套骨架卡"，双重表面 + 双重内边距。判据：**这个壳在加载完成后还在吗？**
  在 → 它是真实卡片，骨架直接放进去；不在 → 它是骨架专用壳，用 `SkeletonCard`。





### 7.7 StreamingTextReveal 流式揭示

| 属性 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `Text` | string | `""` | 绑定的流式文本 |
| `IsAnimated` | bool | `true` | 关闭后退化为静态整段（尊重减少动效偏好） |
| `FontSize` | double | 继承 | 显式暴露，因为 `Decorator` 不带字体属性 |
| `FontWeight` | FontWeight | 继承 | 建议绑 `{DynamicResource WeightBody}` |
| `Foreground` | IBrush | 继承 | 文字色 |
| `LineHeight` | double | 继承 | 行高 |
| `TextWrapping` | TextWrapping | `Wrap` | 换行方式 |

**接入条件**：只有**纯文本**内容走本控件。判定统一收敛在 `ContentFormatDetector`：

| 内容 | 渲染器 |
|---|---|
| 纯文本（不含 Markdown 语法） | `StreamingTextReveal`（逐单元模糊揭示） |
| 含 Markdown 语法 | `RichTextBlock`（全量重解析，逐字会让开销成倍放大） |

**Markdown 样式集必须用 `MarkdownStyleFluentTheme`**：同包另两个样式集
（`MarkdownStyleStandard` / `MarkdownStyleDefaultTheme`）把前景与代码块底色**写死**成
`Black` / `DarkBlue` / `#ffeeeeee` / `#fffafafa`，暗色模式下正文变成黑字压深底、代码块
闪一块亮白。`MarkdownStyleFluentTheme` 的所有前景/背景都经 `DynamicResource` 取 Fluent
主题资源（如 `TextControlForegroundFocused`），随主题切换自动重算。纯文本分支同理，
`TextBlock` 的前景与字号必须显式绑 `TextPrimaryBrush` / `BodyFontSize`，不能依赖继承。
分析师 JSON 产物经 `AdaptiveCardConverter` 解析器链转成 Adaptive Card，统一由 `AdaptiveCardView` 渲染
（聊天气泡与分析报告页共用同一套，没有第二套原生卡片）。多空文字取色走 `BaseAdaptiveCardParser.DirectionColor`，
遵循 2.5「红涨绿跌」。

当前接入点：聊天气泡（`ChatMessageAdapter.ShowPlainText` / `ShowRichTextFallback` 互斥门控）。

### 7.8 CopyFeedback 复制反馈

| 成员 | 说明 |
|---|---|
| `Text` | 提示文字，默认"已复制" |
| `Trigger` | **触发令牌**：值每变化一次播放一次反馈 |
| `Show()` | 直接调用播放（用于代码后置能拿到实例的场景） |
| 视觉 | 语义底 + 白字 + 对勾，圆角 6 |
| 动效 | 入场 180ms `BackEaseOut`（scale 0.85→1 + 淡入）→ 停留 2500ms → 180ms 淡出 |

**为什么用 `Trigger` 而不是只暴露 `Show()`**：控件的常见位置是 `DataTemplate` 内部
（如聊天气泡），代码后置拿不到实例。改由 ViewModel 递增计数器、控件监听变化，
命令与视图之间无需互相引用。

当前接入点：聊天 AI 回复气泡内的「复制」按钮（`ChatSidebarViewModel.CopyMessageCommand`
→ `ChatMessageAdapter.CopyFeedbackTrigger++`）。

### 7.9 StateOverlayView 状态浮层

加载 / 空 / 失败三态统一入口，绑定 `State` / `Message` / `RetryCommand`。
图标尺寸由 `IconSize` 控制，标题用 `card-title`，说明用 `caption`。
重试按钮为 `btn-secondary btn-sm`（不是主按钮——重试不是页面的主操作）。

### 7.10 对话框

居中卡片规范：宽 **440**（400–480 区间），内边距 **24**，圆角 **12**，无边框。
`MessageDialogWindow` 与 `InputDialogWindow` 均已按此实现。

### 7.11 通知

悬浮通知是唯一允许 `ShadowModal` 的常规场景。无边框，圆角 12，
图标为语义色圆角块，正文 `BodySmFontSize` + `WeightEmphasis`。

### 7.12 选择控件：开关 / 复选框 / 单选

判据有两条，**正交**，任一命中复选框条件就用复选框：

> **① 提交时机**：改动立即持久化 → 开关；需要「保存 / 下一步」才生效 → 复选框。
> **② 选择形态**：一组里可多选、或属于可选附加项、或需要表达「部分选中」→ 复选框。

依据是微软 Fluent 规范（Avalonia 的 FluentTheme 复刻的就是这套）：

> **Toggle switch**：binary operations that take effect **right after** the user flips it.
> **Checkbox**：you can **delay committing** a check box interaction (as part of a form submit),
> while you should **immediately commit** a toggle switch interaction. Also, **only check boxes
> allow for multi-selection**.
> — [Guidelines for toggle switch controls](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/toggles)、
> [Check boxes](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/checkbox)

注意微软的用词：**复选框是 status，开关是 action**。反过来说（开关管状态、复选框管动作）是错的——
"开关看起来更像一个状态指示器"是纯视觉直觉，不构成理由。

**判定表**

| 场景 | 控件 | 依据 |
|---|---|---|
| 列表行内、一拨即落库 | `ToggleSwitch` | ① 立即提交 |
| 页面有「保存」按钮 | `CheckBox` | ① 延迟提交 |
| 一组里可多选（指标叠显、分析师角色） | `CheckBox` | ② 多选 |
| 可选附加项（nice to have） | `CheckBox` | ② 微软明确建议 |
| 父项控制一组子项、需表达「部分选中」 | `CheckBox` + `IsThreeState` | ② 仅复选框支持半选 |
| 三个及以上互斥值 | `RadioButton` | 二选一不得用两个开关拼 |
| 门控子表单的总开关 | **跟随所在页面的提交模型** | 不能为了「像开关」改成即时生效 |

**三条禁令**

1. 不得把复选框当 on/off 开关用，也不得用它执行命令（微软原文：*Don't use a check box as an
   on/off control or to perform a command; instead, use a toggle switch.*）
2. 不得用两个开关拼出二选一，互斥一律 `RadioButton`。
3. 开关只表达「开 / 关」**这一个维度**的两态。候选值达到三个及以上时它不再成立，必须换控件。
4. **开关的"危险方向"必须二次确认，"安全方向"必须零确认。** 停下永远不能有摩擦——把安全动作
   藏在一个确认框后面，等于在紧急时刻拖慢用户。参见交易监控开关：开启要确认，关闭直接停。

**技术事实（决定了这条规范只能靠约定维持）**

`CheckBox` 与 `ToggleSwitch` 都继承 `ToggleButton`，都具备 `IsChecked`(=`bool?`) 与
`IsThreeState`，能力完全等价。Teams UI Kit 的说法是「In Fluent UI, toggle is a type of checkbox」。
因此选型**纯粹是语义约定**，代码无法强制，只能靠评审与自检清单兜住。

**与框架文档的冲突**：Avalonia 官方控件目录对 CheckBox 的描述是「use a checkbox for binary user
decisions, such as **feature toggles**」，与微软相反。本项目以微软 Fluent 为准。

**视觉与排布约束**

- 开关宽度固定 **48**（`PriceAlertPageView` 列表行内取值）；复选框不额外约束宽度。
- 桌面设置行要"标签在左、控件在右"，必须自己用 `Grid ColumnDefinitions="*,Auto"` 排布。
  Avalonia Fluent 的 `ToggleSwitch` 模板把 `Content` 放在轨道**上方**
  （`PART_ContentPresenter` 位于 `Grid.Row=0`，下边距 6），把文案塞进 `Content`
  会得到"文字在上、开关在下"的堆叠形态，不能当行内标签使用。
- 开关外观见 7.14；复选框为 Fluent 默认外观，`FormStyles.axaml` 只统一了字号与前景色。

**当前用法盘点**

| 位置 | 控件 | 判据 | 结论 |
|---|---|---|---|
| 设置页九处（通知 / 简报 / 评级告警 / 免打扰 / 飞书推送 / Web Search / 知识库 / 逐分析师模型 / 分析师角色） | `CheckBox` | ① 草稿，底部「保存」提交 | 正确 |
| 价格告警 · 规则行启用 | `ToggleSwitch` | ① `Mode=OneWay` + Command 即时落库 | 正确 |
| 价格告警 · 页签（规则 / 历史） | `Button.underline-tab` + `Classes.selected` | — | 正确（页签不是选择控件，见 7.13） |
| 交易页 · 交易监控开关 | `ToggleSwitch` | ① 一拨即生效；开启先确认、关闭零确认 | 正确（危险方向二次确认，安全方向无摩擦） |
| MCP 服务器 · 列表行启用 | `ToggleSwitch` | ① 列表行内即落库（行内 `PointerPressed` 阻止 ListBox 选中） | 正确 |
| 价格告警 · 新建规则表单（一次性 / 确认级） | `CheckBox` | ① 表单字段 | 正确 |
| 资产页 · 图表指标叠显（MA / BOLL / VOL / MACD / KDJ / 图例） | `CheckBox` | ② 多选集合 | 正确 |
| MCP 服务器 · 允许全部工具 / 工具勾选 | `CheckBox` | ① 服务器编辑表单 + ② 多选 | 正确。启用状态已移到列表行开关，表单内不再重复 |
| 策略列表 · 暂停 / 启动 | `Button`（按状态显隐） | — | **不可改开关**：`StrategyStatus` 有 Active / Paused / Completed / Failed 四态，开关只能表达两态 |
| 主题三选 | `RadioButton` | 互斥 | 控件正确，但 setter 内直接 `ApplyTheme()`，与同页其余设置语义不一致，见下 |
| 新建策略 · 影子模式 / 定投止损清仓 | `CheckBox` | ① 草稿，点「创建策略」才提交 | 正确 |

**已知不一致（记录在案，暂未修改）**

主题选择立即生效（`SettingsPageViewModel` 的 `IsThemeDefault/Light/Dark` setter 内直接
`ApplyTheme`），而同页通知、简报、告警全部要保存。同一页面并存两种提交模型，
比"开关还是复选框"更容易被用户察觉。主题即时生效有正当理由（用户要立刻看到效果），
保留现状；但**不要**以此为先例给其他草稿表单改成即时提交。


### 7.13 页签（下划线指示条）

页签只有一种实现：全局样式 `Button.underline-tab`（`ButtonStyles.axaml`）。
选中态 = 强调色文字 + 底部 2px 强调色指示条（独立图层，不挤压文字），
未选中 = `TextSecondaryBrush`，hover = `TextPrimaryBrush`。

用法：一组 `Button` 包在 `CardBackgroundBrush` 圆角卡里，`Classes.selected` 绑 VM 的
`IsXxxTabSelected`，点击走 `Command` + `CommandParameter`。

**不要**每页各写一套 `ControlTemplate`（此前交易页与告警页各有一份，内边距与字重都不一致），
也**不要**用 `TabControl` + 自定义 `TabItem` 模板模拟（会重新引入 Fluent 的胶囊与焦点方框）。

页签栏**固定在内容区顶部**，不放进页面的 `ScrollViewer` —— 否则滚到列表底部就切不了页签。

### 7.14 ToggleSwitch 开关

全库统一主题在 `Resources/Styles/ToggleSwitchStyles.axaml`，以
`x:Key="{x:Type ToggleSwitch}"` 声明 `ControlTheme`，并注册在 `App.axaml` 的
`Application.Resources`。三处使用点：价格告警规则行启用、交易页监控开关、MCP 服务器列表行启用。

**为什么注册在 Resources 而不是 Styles**：`Application.TryGetResource` 的实现是

```csharp
return (_resources?.TryGetResource(key, theme, out value) ?? false) ||
       Styles.TryGetResource(key, theme, out value);   // Resources 先于 Styles
```

`Resources` 先被查找，所以同名 `ControlTheme` 会盖住 `FluentTheme` 里的内置版本。
这是官方文档推荐的换肤方式（[Control themes](https://docs.avaloniaui.net/docs/basics/user-interface/styling/control-themes)：
*control themes are the right choice when you need to swap out a control's complete look*）；
在 `Style Selector="ToggleSwitch"` 里直接 `Setter Property="Template"` 也能达到效果，
但会绕开 ControlTheme 的"整体替换"语义，不推荐。

| 项 | 值 |
|---|---|
| 几何 | 轨道 40×20，圆点 16×16，圆角 10 / 8 |
| 关 | 轨道 `SwitchTrackOffBrush`（浅 `#DDE1E7` / 暗 `#3A3A3A`），圆点近白 |
| 开 | 轨道 `AccentBrush`，圆点近白 |
| hover / pressed | 关进 `SwitchTrackOffHoverBrush`；开进 `AccentHoverBrush` / `AccentPressedBrush` |
| 焦点 | `FocusRing` 双环（与输入框同几何），打在轨道 Border 上 |
| disabled | `Opacity` 0.4 |
| 圆点动效 | 位移 220ms（无位置过冲）→ 拉伸 90ms → 回落 200ms `BackEaseOut`（性格 D，见 6.4） |

**几何必须与 Fluent 模板契约一致**：`ToggleSwitch.cs` 用
`PART_SwitchKnob.Bounds.Width` 计算 `Canvas.Left`，所以 `PART_SwitchKnob` 必须宽 20、
`PART_MovingKnobs` 必须 20×20，否则位移算错、圆点会停偏。

**为什么保留 `Content`、不提供 `OnContent` / `OffContent`**：仓库内零使用点，而 Fluent
默认值 `"On"` / `"Off"` 会把英文贴在开关旁，`Width=48` 的行内开关还会被压扁。
`Content` 仍在开关**上方**（与 Fluent 一致），策略配置页带说明文字的开关不会重排。

**何时用开关**见 7.12。

### 7.15 CelebrationBurst 收藏烟花

| 项 | 值 |
|---|---|
| 触发 | `Play()`，重复调用重置进度 |
| 视觉 | 12 粒光点径向扩散 + 简易阻尼 + 后半程淡出，颜色只用 `AccentBrush` |
| 时长 | 520ms（`BurstDurationMs`），16ms 帧驱动 |
| 命中 | `IsHitTestVisible=false`，不拦截点击 |

**全库唯一挂载点：资产详情页的「加入自选」。** 刻意不扩散，理由：

- 首页热门资产的收藏会先弹确认框再弹成功框（`HomeAssetService.AddToFavoriteAsync`），
  烟花会被模态窗压住、延迟到关窗后才播，等于白做。
- 自选页只有删除，没有新增。
- **取消收藏不播**：那会读成"操作被撤销得很值得庆祝"。

实现用单个 `Render` + 定时器驱动，而不是给 12 个 `Ellipse` 各挂一套 Animation：
粒子每帧位置都在变，用动画系统反而要生成大量过渡对象。

---

## 八、页面结构

### 8.1 主框架（MainWindow）

```
┌────────────┬──────────────────────────────────────────┐
│            │ 返回窄条（仅二级页，占布局高度，不浮层）      │
│ 侧边导航    ├──────────────────────────────────────────┤
│ 240px      │                                          │
│ 品牌        │ 内容区（12 列栅格，页面内边距 24）          │
│ 市场切换    │  · 首页头部含标题 + 行情条                  │
│ 主导航      │                                          │
│ …          │                                          │
│ 设置/关于   │                                          │
└────────────┴──────────────────────────────────────────┘
```

- 导航项高度 40，选中态为强调色浅底 + 强调色标签（**无左侧装饰竖条**）。
- **无全局顶栏**（v3）：首页行情条（各指数均分整行宽度）归属首页"市场概览"标题区；
  二级页面返回入口是内容区顶部的**布局内窄条**（`BackBarMargin`，根页面折叠为零高度），
  不用浮层——浮层会遮挡页面左上角内容；其余根页面无任何顶部占用。
- 窄窗（< 1024px）导航收起为 72px 图标轨，图标居中。

### 8.2 页面统一结构

页面**不设页头**。标题由导航承担，页面内容直接进入区块卡片：

```
────────────────────────────────────────
区块卡片（card，内边距 20，间距 24）
```

- **根页面不写标题**：左侧导航的选中高亮已经表达了"当前页面"，页面上再写一遍
  "设置""关于"是重复信息，只是把内容往下推。
- **二级页面不写标题**：返回窄条（`BackBarMargin`）里已经有"← + 页面标题"，
  页面内再写标题会出现一屏两个标题。
- **唯一例外是自选页**：它用 `PageHeaderView` 只是为了承载右侧的
  「卡片视图 / 表格视图」切换操作槽，标题是操作槽的搭档而非装饰。
- **页面标题不进卡片**：`CardView.Header` 只用于区块级标题（如"热门资产"）。
- **二级页面返回区**：返回按钮 + 页面标题为布局内窄条
  （`BackBarMargin`，根页面折叠为零高度），禁止浮层式返回——浮层会遮挡页面左上角内容。
- **首页头部**：三行各占一行——行情条（各指数均分整行宽度）/ 搜索框（占满整行）/
  最近查看（独占一行，超出横向滚动）。搜索框与最近查看**不得并排**：
  并排会把搜索框压窄、把最近查看挤成一条残缺的窄带。
- **页面级操作放区块标题行**：如告警页「刷新」挂在"告警规则"标题行右侧，
  而不是挂在页头操作槽——页头已不存在，且"刷新某个列表"本就属于列表自身。

### 8.3 各页面要点

| 页面 | 布局 | 关键约束 |
|---|---|---|
| **首页** | 行情条 / 搜索框 / 最近查看（各占一行）→ 7:5 双列（热门资产 / 7×24 快讯） | 快讯条目用可悬停行，不用独立卡片（避免卡片套卡片） |
| **自选** | 标题 + 视图切换 → 卡片网格 / 高密度表格 | 价格用 `TitleFontSize` 等宽数字作卡片视觉锚点 |
| **资产详情** | 顶部单行（名称+代码 / 价格+涨跌 / 操作）→ K 线主栏 + 右栏 360px（行情数据 + 盘口） | 价格用 `DisplayFontSize`（32px），代码徽章用中性底 |
| **AI 分析** | 报告主栏（顶部只留 `⋮` 更多操作）+ 常驻伴侣分屏（360px，间隔 16） | 报告区不重复标题，标题只由主窗口返回条提供；伴侣面板是独立圆角面板（`CardBackgroundBrush` 表面，不靠左描边割裂）。报告页 hero 区承担标的信息行（代码 + 生成时间）与置信度；评级徽章是**唯一**携带数据语义色的元素，综合评分与维度进度条一律走主文字 + `AccentBrush`。分析师完整结论收在报告页「分析师明细」折叠区块，不要求用户去伴侣面板翻。**已知项**：窄窗（720px）下 360px 伴侣面板会把报告主栏压到约 300px，暂未做面板自动收起 |
| **交易** | 页签栏 → 页签内容（状态主卡 → 7:5 双栏） | 见 8.4；页签选中用下划线指示条，状态主卡承载监控开关与紧急动作 |
| **告警规则** | 固定页签栏（规则 / 历史）→ 创建卡 → 规则列表（可滚动） | 见 8.5 |
| **设置** | 手风琴分组卡片 + 底部白色操作栏 | 分组卡片用全局 `Expander.section-card`，无边框；页面底色为 `PageBackgroundBrush`，底部栏用 `CardBackgroundBrush` |
| **AI 选股** | 模式卡片 → 输入区 → 结果区 | 模式卡用 `CardBackgroundBrush`（与页面底色拉开层次）；提示条用语义底，不用 ⚠️ 等 Emoji |

### 8.4 交易模块（三个页签共用一套骨架）

交易模块有三个页签（策略配置 / 实时监控 / 交易历史）。它们共享同一个父级栅格与
行模板，**不允许各自发明布局**——历史上三个页签分别用了 `ScrollViewer` / `Grid` /
三行 `Grid` 三种根布局，间距与圆角各不相同。

**共享骨架（三个页签逐条对齐）：**

1. **页签内标题**：`card-title` + `caption`，间距 `TinySpacing`，底部 `BottomMargin`。
   父级 `TradingPageView` 已提供 `PagePadding`，页签内**不再重复内边距**。
2. **栅格**：内容区用 `PageGrid`（12 列）+ `ColumnSpacing="{StaticResource GridGutter}"`。
   常用跨度 `7:5`（主区 : 侧区）。不要写 `ColumnDefinitions="*,12,*"` 这类比例 + 硬编码 gutter。
3. **卡片**：一律 `CardView`（圆角 12、内边距 20）。筛选栏也是卡片，不要用 8 圆角的裸 `Border`。
4. **行模板**：策略 / 持仓 / 挂单三种行各有唯一实现，放在
   `Views/Components/Trading/`，页签只引用不复制：
   - `StrategyRowView`（DataContext = `TradingStrategy`，操作按钮经 `Actions` 注入）
   - `PositionRowView`（DataContext = `PositionInfo`）
   - `OrderRowView`（DataContext = `ExchangeOrderResult`，含成交进度条）
   行容器统一 `SurfaceVariantBrush` + `TinyCornerRadius` + `SmallCardPadding`。
5. **三态**：首屏骨架（绑定 `IsInitialLoading`，结构克隆真实布局）+ `StateOverlayView`
   （`ShowLoading="False"`）。空态由各分区自行提示。
   策略配置页的空列表**不算无数据页**——新建表单必须始终可操作，浮层只承担失败重试。

**实时监控页的布局（唯一有仪表盘性质的页签）：**

```
┌────────────────────────────────────────────────────────┐
│ 状态主卡（12 列）：状态圆点/模式标签 | 4 项指标 | 开关+动作 │
├───────────────────────────┬────────────────────────────┤
│ 风控指标（7 列）           │ 策略绩效（5 列）             │
│ 合约强平风险（有数据才显）  │ 活跃策略（5 列）             │
│ 当前持仓（7 列）           │ 未完成订单（5 列）           │
└───────────────────────────┴────────────────────────────┘
```

- **状态与动作并入同一张主卡**：监控开关、余额详情、刷新、紧急停止、全部平仓都在主卡的
  右侧操作区。**不要**把它们排成主卡之外的一列图标按钮——那会与卡片高度不齐，读起来像游离的工具栏。
- **四项指标横排在主卡左侧**，不再单独占一行四张卡片：它们是"当前状态"的一部分，
  与监控开关同屏才能形成"我现在是什么状态、能做什么"的完整判断。
- **风控进度行的标签列固定宽度**（`RiskMetricLabelWidth`，页内资源），
  保证四条进度条左端对齐；不要用 `Auto`（每条标签字数不同会让进度条参差不齐）。
- **紧急动作的危险方向用 `btn-danger`**（全部平仓），安全方向用 `btn-secondary`（紧急停止）。

### 8.5 告警规则页（PriceAlertPage → 规则 / 历史）

```
┌────────────────────────────────────────────────────────┐
│ 页签栏（规则 / 告警历史）— 固定，不随内容滚动            │
├────────────────────────────────────────────────────────┤
│ 滚动区：                                                 │
│   创建规则卡（CardBackgroundBrush）                       │
│     ├ 4 字段（标签在上、控件在下，2×2 网格）              │
│     ├ 仅提醒一次 + 创建告警（独立一行）                   │
│     └ 高级选项（裸 Expander，默认收起）                   │
│   列表标题行：标题 + 说明 + 刷新 + 条数                   │
│   规则行 × N                                             │
└────────────────────────────────────────────────────────┘
```

页签栏固定、**表单与列表同处一个滚动区**：只固定页签，是因为它是页面级导航；
把创建卡也固定住会在 600px 窄窗下把列表压到不足 100px。

- **创建卡必须用 `CardBackgroundBrush`**。它曾经用 `SelectedBackgroundBrush` 铺底，
  整页因此出现第二块高饱和蓝底，输入框的 `SunkenBackgroundBrush` 在蓝底上也读不出凹陷层次。
- **字段用"标签在上、控件在下"的四列网格**，不要把标签与控件混排成一行——
  混排会让每列宽度由标签字数决定，行与行之间无法对齐。主按钮从字段网格里拿出来，
  与「仅提醒一次」并排成独立一行，避免按钮被挤成与输入框同宽的窄条。
  指标类规则的「K线周期」放第二行首列，不要挤进常用字段那一行。
- **规则行里的状态标签**（一次性 / 持续 / 已触发）用 7.5 的 12% 语义底 + 同色字，
  不用语义色实底。
- **「跌幅超」用绿色、「涨幅超」用红色是正确的**：本项目以 A 股与国内用户习惯为准，
  `BullishBrush` 是红、`BearishBrush` 是绿（见 2.5）。不要按欧美惯例"修正"成红跌绿涨。
- 列表行容器是 `ItemsControl` + `Border`（无整行选中），行内开关**不需要** `PointerPressed` 拦截。
- 骨架是真实规则行的结构克隆：同列宽、同 `Padding`、同开关（40×20）与删除按钮尺寸。

---

## 九、图标

- 全部为 SVG 矢量资源，位于 `Assets/Images/`。
- **v3 统一配色**：全部图标只用 `#0066FF`（强调）与中性灰（`#5C6470` 未选中 / `#8A93A0` 装饰），
  语义色仅用于 `icon_warning` / `icon_danger` 等语义图标。
- 移除了旧版的琥珀 `#F7B548`（选中态）、紫 `#512BD4`、浅蓝 `#42A5F5`、橙 `#FF6B35`。
- 图标尺寸走 `DefaultIconSize`(24) / `SmallIconSize`(20) / `TinyIconSize`(16) 令牌。

---

## 十、可访问性与工程约束

- **不吞异常**，不用 Emoji 表示状态（全部改用 SVG 图标）。
- 所有主题感知资源用 `{DynamicResource}`；`{StaticResource}` 不会随主题切换刷新。
- `BoxShadow` 只能写在 `Border` / `ContentPresenter` 上。
- `FontWeight`、配色、字号一律走令牌，禁止字面量。C# 创建的控件同样适用
  （用 `DynamicResourceExtension` 绑定，不要写 `FontWeight.Bold` / `FontSize = 12`）。
- 新增页面不得引入第六档字号或第二个强调色。
- 改动样式层后必须 `dotnet build MarketAssistant.slnx -c Debug`，并跑
  `tools/verify-resource-keys.ps1`（它会同时检查 XAML 标记扩展与 C# 字符串键）。

---

## 十一、自检清单

提交 UI 改动前逐条核对：

- [ ] 没有新增 `BorderBrush` + `BorderThickness` 的描边组合
- [ ] 没有新增第二个强调色或彩色装饰图标
- [ ] `SelectedBackgroundBrush` 只用在可选中元素的局部，没有拿它铺满卡片或区块
- [ ] 语义色只做 12% 浅底标签或文字，没有实心色块压白字
- [ ] 页签用的是全局 `Button.underline-tab`，没有页面私有的页签 `ControlTemplate`
- [ ] 字重全部为 `{DynamicResource Weight*}`
- [ ] 字号只用了五档令牌（32/24/16/14/12）
- [ ] 等宽字体只出现在数字上
- [ ] hover 只改色阶，没有 `scale` 或 `TranslateTransform` 位移（开关圆点与收藏烟花除外，见 6.4）
- [ ] 骨架是真实卡片的结构克隆（同 Padding / Spacing / 行高）。一个 `SkeletonLines`
  只放一段等高灰条；多段不同字号用多个 `SkeletonLines`，没有用字符串编码多段
- [ ] 输入框聚焦是双环，不是描边变色
- [ ] 选择控件符合 7.12：有保存按钮的页面用复选框，即时生效才用开关
- [ ] 没有用两个开关拼出二选一（互斥一律 `RadioButton`）
- [ ] 阴影只出现在下拉菜单 / 弹窗 / 悬浮通知
- [ ] 深色模式看过一眼，字重是否发粗
- [ ] 窄窗（< 1024px）导航收起且图标居中
- [ ] `tools/verify-resource-keys.ps1` 通过（XAML 与 C# 字符串键均无失效引用）

---

## 附录 A：令牌文件索引

| 文件 | 内容 |
|---|---|
| `Resources/Styles/Colors.axaml` | 强调色、中性阶、语义色、主题字典 |
| `Resources/Styles/Spacing.axaml` | 字号、字重（主题化）、间距、圆角、控件尺寸 |
| `Resources/Styles/Elevation.axaml` | 阴影层级、焦点双环 |
| `Resources/Styles/Motion.axaml` | 时长、缓动、流式动效参数 |
| `Resources/Styles/ButtonStyles.axaml` | 按钮全部变体与状态；页签 `Button.underline-tab` |
| `Resources/Styles/FormStyles.axaml` | 输入类控件 |
| `Resources/Styles/CardStyles.axaml` | 卡片与表面、手风琴壳、涨跌瓦片（无通用语义面板类） |
| `Resources/Styles/ListStyles.axaml` | ListBoxItem 基础与 DataGrid |
| `Resources/Styles/SkeletonStyles.axaml` | 骨架屏（`skeleton-line-*` 行高基元） |
| `Resources/Styles/TextStyles.axaml` | 排版类 |
| `Resources/Styles/ToggleSwitchStyles.axaml` | ToggleSwitch 全局主题（弹性圆点） |
| `Views/Controls/CardView.axaml` | 卡片控件模板 |
| `Views/Controls/PageGrid.cs` | 12 列内容栅格基元 |
| `Views/Controls/StreamingTextReveal.cs` | 流式揭示控件 |
| `Views/Controls/CopyFeedback.cs` | 复制反馈控件 |
| `Views/Controls/KnobPill.cs` | 开关圆点（拉伸 → 回落） |
| `Views/Controls/CelebrationBurst.cs` | 收藏烟花粒子 |
| `Views/Controls/ContentFormatDetector.cs` | 纯文本 / Markdown 判定（选择渲染器用） |
| `Views/Controls/SkeletonList.cs` | 骨架列表（重复 N 个骨架） |
| `Views/Controls/SkeletonCard.cs` | 骨架卡片（一个骨架的壳 + 内容） |
| `Views/Controls/SkeletonLines.cs` | 骨架一段等高灰条 |
| `Views/Components/Trading/StrategyRowView.axaml` | 策略行（配置页 / 监控页共用） |
| `Views/Components/Trading/PositionRowView.axaml` | 持仓行（含强平价、杠杆、未实现盈亏） |
| `Views/Components/Trading/OrderRowView.axaml` | 挂单行（含成交进度） |
