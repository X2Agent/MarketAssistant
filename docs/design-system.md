# MarketAssistant UI 设计系统

> **版本**: 2.4  
> **平台**: Avalonia 11.x 跨平台桌面应用  
> **产品**: 金融市场数据终端 (A股 + 虚拟币)  
> **设计方向**: 清晰专业风 — Bloomberg Terminal 的严谨 × 现代 SaaS 的优雅
>
> **v2.4 变更（K线图主题化 + on-color 收敛轮，代码与文档同步）**：
> 1. K 线图主题化落地：`kline_chart.html` 新增 `setTheme('dark'|'light')` 实现（此前注释声称"由 setTheme 动态切换"但函数从未实现，深色主题下 K 线区永久白底）；`KLineChartView` 按 `ActualThemeVariant` 在导航完成后注入并监听 `ActualThemeVariantChanged` 联动；深色 palette 对齐设计系统（`#111722/#1E2636/#8894A8/#5A6680`，涨跌色两主题一致），兜底 HTML 与 echarts loading 配色同步修正（原硬编码 `#4d90fe/#000/白遮罩/#e74c3c`）；
> 2. 新增 on-color 语义 Token `TextOnEmphasisBrush`（`text-on-emphasis`，#FFFFFF 明暗同值）：全部视图/样式约 22 处 `Foreground="White"` 硬编码收敛为 Token 引用（含 `ButtonStyles` 的 btn-primary/btn-info 定义）；
> 3. 动效时长越阶梯值收敛：`CardStyles` 8 处 0.2s、`HomePageView` 0.12s → 150ms；`SkeletonStyles` 0.8s → 600ms。注：Transition.Duration 为 TimeSpan 类型，Avalonia XAML 不支持 TimeSpan 资源 Token，Duration 以阶梯字面量维护（`Spacing.axaml` 有注记）；
> 4. `TextStyles.axaml` 兼容区清理：删除无消费方的 `featureTitle` 与重复定义的 `feature-title`（保留 `CardStyles.axaml` 中被 AboutPageView 使用的同名样式）；
> 5. 本文档补齐此前未登记的颜色 Token（明暗交互态、`ItemBackgroundBrush`、`ModeCardSelectedTextBrush`、`DangerBackgroundBrush`、`text-on-emphasis` 等），并新增「K 线图配色」小节；
> 6. `docs/配色预览.html` 修正页面文本色 `#E8ECF1` → `#E4EAF5`（原与规范不一致），新增 K 线图主题预览区，版本升至 v2.4。
>
> **v2.3 变更（偏差清理落地轮，代码与文档同步收敛）**：
> 1. 功能语义色收敛完成：`Success/Warning/Error/Info` 及 `*Hover/*Pressed` 改为 Tailwind 系目标值，与涨跌色的同值语义混叠已消除（第二章注记更新）；
> 2. 遗留脏色清除：`#dc3545`（`RequiredFieldColor` / `DialogDangerBackground`）与 `#007bff`（`PrimaryButtonColor`）及其画刷整体删除（均无消费方）；
> 3. 补齐 `BullishTagBackgroundBrush` / `BearishTagBackgroundBrush`（约 12% 透明度标签背景）；暗/亮 `SelectedBackgroundBrush` 统一为品牌蓝 15% 填充；
> 4. `PrimaryDark` 二选一裁决：采纳规范值 `#0D47A1`；
> 5. 亮色主题文本色对齐 `#1A1D26 / #6B7280 / #9CA3AF`；通知窗口专用色迁入蓝调深色体系（`#161C2A / #1E2636`）；
> 6. 6 个视图中的 emoji 全部替换为 feather 风格 SVG 线性图标（新增 `icon_*.svg` 资源，语义配色）；
> 7. 修复 `PriceChangeColorConverter` 硬编码 Flat UI 色（`#e74c3c/#2ecc71/#6c757d`）→ 改为读取设计系统资源并带兜底；支持 `ConverterParameter=tag` 返回 12% 背景画刷；
> 8. 合并重复语义色组：`*Light/*Background/*Border/*Text` 硬编码组整体删除，消费方（ButtonStyles / AdaptiveCardView / ApiKeyConfigView / AssetSelectionPageView）全部改指主题感知 `*Panel*` 组；
> 9. 补齐 `BorderActiveBrush`（暗 `#42A5F5` / 亮 `#1976D2`）与 `DarkOverlayBrush` 遮罩 Token，聚焦边框统一改用 `BorderActiveBrush`；
> 10. 字号/圆角阶梯收敛完成：15/20/24 → 14/18/22，圆角 10/16 并入 8/12（`LargeCornerRadius` Token 删除）；
> 11. 阴影 Token 收敛：`ChatSidebarView` 本地 4 个 BoxShadow 与 `AgentAnalysisPageView` 侧栏阴影/硬编码遮罩全部迁至全局 Token（`SidebarBoxShadow` / `InputAreaBoxShadow` / `UserBubbleBoxShadow` 新增）；
> 12. 涨跌标签 12% 背景落地首个渲染点（收藏页涨跌幅标签：色块背景 + 同色文字，替代实色底白字）；
> 13. `docs/配色预览.html` 重写为 v2.3 单一方案预览页，不再保留旧方案对比内容。
>
> **v2.2 变更（实施现状盘点，与 `Colors.axaml` / `Spacing.axaml` 实际值对齐）**：
> 1. 新增「十一、实施现状与偏差清单」，逐项登记代码实现与本文档规范的差异及待办；
> 2. 布局尺寸与动效时长改为记录实际实现值（导航栏 72px、动效 150/300/600ms、最小窗口 800px）；
> 3. 字号与圆角阶梯补充实际存在但超出阶梯的值（15/20/24、10/16），并标注收敛方向；
> 4. 消除 v2.1 头部与第八章响应式断点的矛盾：统一为两档 ≥1100px / <1100px（第八章已重写）；
> 5. 功能语义色登记"双套并存"问题（Material 系 vs Tailwind 系），明确收敛策略。
>
> **v2.1 变更（与 design-prototype.html v5 的裁决）**：
> 1. 深色背景以本文档色值为准（`bg-root #0A0E17` / `bg-surface #111722` / `bg-elevated #161C2A`），原型中更深的 `#080C14/#0E1320/#141B2A` 弃用；
> 2. 导航活跃指示条采用**左侧 3px 竖条**，不采用原型的底部横条；选中项背景使用 `bg-selected` 浅色填充而非整块品牌蓝；
> 3. 首页不放 K 线区，采用 Bento Grid（热门标的置左最宽 + 快讯 + 最近查看）；K 线保留在资产详情页。原型中的"AI 市场概览横幅"待数据源就绪后再引入；
> 4. 涨跌标签一律带约 12% 透明度背景（`BullishTagBackgroundBrush` 等），靠色块而非纯文字色区分语义；
> 5. 响应式简化为两档：≥1100px 完整布局，<1100px 单列隐藏详情面板；
> 6. TopBar 常驻：标题/返回 + A股/虚拟币分段切换器（复用 Ctrl+M 同一逻辑）；行情 ticker 需真实指数数据源，无数据时整段隐藏。

---

## 一、设计理念

### 设计目标

MarketAssistant 是一款面向专业投资者的金融数据终端。界面设计需同时满足：

| 维度 | 目标 | 对应策略 |
|------|------|---------|
| **可扫描性** | 用户在 3 秒内识别关键信息 | 层次分明的排版、颜色编码、网格布局 |
| **数据密度** | 单屏展示尽可能多的有效信息 | 紧凑但不拥挤的间距系统、等宽数字字体 |
| **专业性** | 传达可靠、严谨、值得信赖的感觉 | 深色主题为主、克制的配色、清晰的边框分隔 |
| **可访问性** | 长时间盯盘不疲劳 | 深色背景减少眩光、足够的文字对比度 |
| **跨市场** | A股与虚拟币统一体验 | 市场切换时保持布局一致，仅数据变化 |

### 设计原则

1. **数据优先，装饰其次**: 每一个像素都应服务于信息传递，去除无意义装饰
2. **暗色为主，亮色为辅**: 默认深色主题，提供亮色作为备选
3. **一致性高于个性化**: 相同类型数据使用相同呈现方式
4. **渐进式信息披露**: 概览→详情，点击深入而非一次性堆砌

---

## 二、颜色系统

### 品牌主色

| Token | 色值 | 用途 |
|-------|------|------|
| `brand-blue` | `#1976D2` | 主按钮、选中态、链接、进度条 |
| `brand-blue-light` | `#42A5F5` | 悬停态、辅助强调 |
| `brand-blue-dark` | `#0D47A1` | 按下态 |
| `brand-accent` | `#FF6B35` | 提醒、CTA、特殊标注 |

### 深色主题 (默认)

| Token | 色值 | 用途 |
|-------|------|------|
| `bg-root` | `#0A0E17` | 页面最底层背景 |
| `bg-surface` | `#111722` | 卡片、面板、侧边栏 |
| `bg-elevated` | `#161C2A` | 悬浮卡片、下拉菜单、tooltip |
| `bg-overlay` | `#1B2233` | 对话框遮罩背景 |
| `border-default` | `#1E2636` | 默认边框 |
| `border-subtle` | `#182032` | 细微分割线（卡片内部） |
| `border-active` | `#1976D2` | 聚焦/选中边框 |
| `text-primary` | `#E4EAF5` | 正文、标题 |
| `text-secondary` | `#8894A8` | 辅助说明、标签 |
| `text-tertiary` | `#5A6680` | 禁用态、占位符、页码 |
| `text-on-emphasis` | `#FFFFFF` | 品牌蓝/功能色实底上的前景文字（`TextOnEmphasisBrush`，明暗同值，v2.4） |
| `bg-hover` | `#161C2A` | 列表/按钮悬停背景（`HoverBackgroundBrush`，与 bg-elevated 同值） |
| `bg-pressed` | `#1B2233` | 按下态背景（`PressedBackgroundBrush`） |
| `bg-danger-muted` | `#2D1B1B` | 危险提示容器背景（`DangerBackgroundBrush`） |
| `text-active` | `#42A5F5` | 模式卡片选中文字（`ModeCardSelectedTextBrush`） |

> **v2.4 补记**：`ItemBackgroundBrush`（列表项背景）与 `bg-surface` 同值（暗 `#111722` / 亮 `#FFFFFF`）；`ShadowBrush`（暗 `#80000000` / 亮 `#40000000`）仅作阴影兜底，实际阴影走 BoxShadows Token。

### 亮色主题

| Token | 色值 | 用途 |
|-------|------|------|
| `bg-root` | `#F5F6F8` | 页面最底层背景 |
| `bg-surface` | `#FFFFFF` | 卡片、面板 |
| `bg-elevated` | `#F0F1F3` | 悬浮卡片 |
| `bg-overlay` | `#E8E9EC` | 对话框背景 |
| `border-default` | `#E0E2E8` | 默认边框 |
| `border-subtle` | `#EEF0F4` | 细微分割线 |
| `text-primary` | `#1A1D26` | 正文、标题 |
| `text-secondary` | `#6B7280` | 辅助说明 |
| `text-tertiary` | `#9CA3AF` | 禁用态 |
| `text-on-emphasis` | `#FFFFFF` | 品牌蓝/功能色实底上的前景文字（`TextOnEmphasisBrush`，明暗同值，v2.4） |
| `bg-hover` | `#F5F5F5` | 悬停背景（`HoverBackgroundBrush`） |
| `bg-pressed` | `#EEEEEE` | 按下态背景（`PressedBackgroundBrush`） |
| `bg-danger-muted` | `#FEF2F2` | 危险提示容器背景（`DangerBackgroundBrush`） |
| `text-active` | `#0D47A1` | 模式卡片选中文字（`ModeCardSelectedTextBrush`） |

### 市场语义色 (中国市场标准: 红涨绿跌)

| Token | 色值 | 用途 |
|-------|------|------|
| `color-bullish` | `#F44336` | 上涨、买入、多头 |
| `color-bearish` | `#4CAF50` | 下跌、卖出、空头 |

### 功能语义色

| Token | 色值 | 用途 |
|-------|------|------|
| `color-success` | `#10B981` | 成功、完成、在线状态 |
| `color-warning` | `#F59E0B` | 警告、需要注意 |
| `color-danger` | `#EF4444` | 错误、危险操作 |
| `color-info` | `#3B82F6` | 信息提示 |

> **✅ 实施现状（v2.3 已收敛）**：`Colors.axaml` 中 `Success/Warning/Error/Info` 已统一为上表 Tailwind 系取值，交互状态色（`*Hover/*Pressed`）同步重算为对应明暗档；与 `BullishRed/BearishGreen` 的同值语义混叠已消除。剩余待办见第十章 P2+ 第 13 条（亮色硬编码组与 `*Panel*` 组合并）。

### 状态背景色 (深色)

| Token | 色值 | 用途 |
|-------|------|------|
| `bg-bullish` | `rgba(244,67,54,0.12)` | 上涨标签背景 |
| `bg-bearish` | `rgba(76,175,80,0.12)` | 下跌标签背景 |
| `bg-selected` | `rgba(25,118,210,0.15)` | 选中行/项背景 |

### K 线图配色（✅ v2.4 已落地）

K 线图为内嵌 WebView 页面（`Assets/Raw/kline_chart.html`，klinecharts 9.x 渲染），由 C# 宿主（`KLineChartView`）在导航完成后按 `ActualThemeVariant` 注入 `setTheme('dark' | 'light')`，并监听 `ActualThemeVariantChanged` 联动切换：

| 元素 | 浅色 | 深色 | 说明 |
|------|------|------|------|
| 图表背景 | `#FFFFFF` | `#111722`（bg-surface） | CSS 变量 `--chart-bg`，loading/error 浮层同步 |
| 网格线/分隔线/坐标轴线 | `#E6E9EF` / `#D0D5DD` | `#1E2636`（border-default） | |
| 轴文本/最高最低标注 | `#667085` | `#8894A8`（text-secondary） | |
| 十字线 | `#98A2B3` | `#5A6680`（text-tertiary） | 标签底色 `#1976D2` / `#42A5F5` + 白字（text-on-emphasis） |
| 涨跌色 | `#F44336` / `#4CAF50` | 同浅色 | 红涨绿跌，两主题一致 |
| 平盘 | `#98A2B3` | `#5A6680` | |
| 加载指示 | `#1976D2`（brand-blue） | `#42A5F5`（brand-blue-light） | spinner / 兜底页 echarts loading |
| 错误色 | `#EF4444`（color-danger） | 同浅色 | 原 Flat UI `#e74c3c` 已替换 |

> 兜底 HTML（资产加载失败时的 echarts 页面）配色同步对齐：深底 `#0A0E17`、文本 `#8894A8`、loading 品牌蓝 `#1976D2`、遮罩 `rgba(10,14,23,0.8)`。

---

## 三、排版系统

### 字体族

| 层级 | 字体 | 用途 |
|------|------|------|
| **UI 字体** | `-apple-system, "PingFang SC", "Microsoft YaHei", sans-serif` | 界面文字、标题、按钮 |
| **数据字体** | `"JetBrains Mono", "SF Mono", "Consolas", monospace` | 价格、百分比、成交量、代码 |

> 数据字体必须启用 `tnum` (tabular numbers) 特性，确保数字等宽对齐。

### 字号阶梯

| Token | 字号 | 用途 |
|-------|------|------|
| `text-xs` | 11px | 代码标签、辅助说明、时间戳 |
| `text-sm` | 12px | 次要文本、标签、表格内容 |
| `text-base` | 13px | 正文（默认） |
| `text-md` | 14px | 面板标题、强调正文 |
| `text-lg` | 16px | 区块标题、卡片标题 |
| `text-xl` | 18px | 页面标题、K线图标题 |
| `text-2xl` | 22px | 大标题 |
| `text-3xl` | 28px | 核心指标（价格、涨跌幅） |

> **实施现状（v2.3 已收敛）**：`Spacing.axaml` 实际阶梯为 11/12/13/14/16/18/22/28，与上表一致。原越界值已并入标准档：`MediumFontSize` 15→14、`LargeFontSize` 20→18、`TitleFontSize` 24→22（h2 同步映射到 22px）。

### 行高

| 上下文 | 行高 |
|--------|------|
| 正文段落 | 1.6 |
| 列表项/表格 | 1.4 |
| 标题 | 1.2 |
| 数据数字 | 1.0 |

### 字重

| 字重 | 数值 | 用途 |
|------|------|------|
| Regular | 400 | 正文 |
| Medium | 500 | 标签、数据 |
| Semibold | 600 | 面板标题、卡片标题 |
| Bold | 700 | 页面标题、核心指标 |

---

## 四、间距系统

采用 **4px 基准** 间距系统，所有间距为 4 的倍数：

| Token | 值 | CSS 变量 | 用途 |
|-------|-----|---------|------|
| `space-1` | 4px | `--space-1` | 图标与文字间距、紧凑元素间距 |
| `space-2` | 8px | `--space-2` | 列表项内边距、标签间距 |
| `space-3` | 12px | `--space-3` | 列表项间距、表单元素间距 |
| `space-4` | 16px | `--space-4` | 卡片内边距、组件间距 |
| `space-5` | 20px | `--space-5` | 区块间距 |
| `space-6` | 24px | `--space-6` | 页面内边距、大区块间距 |
| `space-8` | 32px | `--space-8` | 独立区块间距 |
| `space-10` | 40px | `--space-10` | 页面顶部间距 |
| `space-12` | 48px | `--space-12` | 页面级分隔 |

### 布局尺寸

| Token | 值 | 用途 |
|-------|-----|------|
| 侧边导航栏 | 72px | 图标导航栏宽度（`NavRailWidth`，实际实现值） |
| 右侧面板 | 320px | 详情/盘口/技术指标 |
| 最小窗口宽度 | 800px | 低于此宽度压缩布局（`MainWindowMinWidth`，实际实现值） |
| 最小窗口高度 | 600px | 低于此高度压缩图表区域（`MainWindowMinHeight`） |

---

## 五、圆角与阴影

### 圆角

| Token | 值 | 用途 |
|-------|-----|------|
| `radius-sm` | 4px | 按钮、标签、输入框 |
| `radius-md` | 6px | 小型卡片、下拉菜单 |
| `radius-lg` | 8px | 卡片、面板 |
| `radius-xl` | 12px | 大型面板、图表容器 |
| `radius-full` | 999px | 药丸形状、Badge |

> **实施现状（v2.3 已收敛）**：圆角 10px（`MediumCornerRadius`）并入 8px、16px（`LargeCornerRadius`）并入 12px（Token 已删除，消费方改指 `DefaultCornerRadius`）。实际阶梯为 4/6/8/12/28(FAB)，与上表一致。

### 阴影 (仅亮色主题使用)

| Token | 实现键 | 效果 | 用途 |
|-------|--------|------|------|
| `shadow-card` | `CardBoxShadow` | `0 2 8 0 #15000000` | 默认卡片 |
| `shadow-subtle` / `shadow-small` | `SubtleBoxShadow` / `SmallBoxShadow` | `0 2 8 0 #10000000` / `0 1 3 0 #20000000` | 气泡、头像等轻量元素 |
| `shadow-elevated` | `ElevatedBoxShadow` | `0 8 32 0 #30000000` | 悬浮卡片、下拉菜单 |
| `shadow-overlay` | `NotificationBoxShadow` | `0 4 16 0 #60000000` | 通知窗口 |
| —（聊天/浮层专用） | `SidebarBoxShadow` / `InputAreaBoxShadow` / `UserBubbleBoxShadow` | 方向性阴影 | 侧栏、输入区、用户气泡 |

> 深色主题中，阴影效果不明显。通过 `border` 和背景色差 (`bg-elevated`) 来区分层级。

---

## 六、动效系统

| Token | 时长 | 缓动 | 用途 |
|-------|------|------|------|
| `transition-fast` | 150ms | `ease` | 悬停色变、选中态切换 |
| `transition-normal` | 300ms | `ease` | 面板展开/折叠、页面切换（`MediumAnimationSeconds`，实际实现值） |
| `transition-slow` | 600ms | `ease-out` | 模态窗口进出（`SlowAnimationSeconds`，实际实现值） |

> **实施现状（v2.4 已收敛）**：全部 `Transition.Duration` 字面量收敛至阶梯值——原 `CardStyles` 8 处 0.2s、`HomePageView` 0.12s 归入 150ms（transition-fast），`SkeletonStyles` 0.8s 归入 600ms。因 Avalonia XAML 不支持 TimeSpan 资源 Token，Duration 以阶梯字面量维护（`Spacing.axaml` 内有注记说明）。

### 动效原则

- 仅对 `opacity`、`transform`、`background-color`、`border-color` 做动效
- 禁止对 `width`、`height`、`margin`、`padding` 做动画（性能差）
- 所有循环动画（如加载指示器）需可被 `prefers-reduced-motion` 暂停
- 不使用弹性/回弹缓动（专业金融工具的克制感）

---

## 七、组件规范

### 7.1 按钮 (Button)

| 变体 | 用途 | 样式 |
|------|------|------|
| **Primary** | 主要操作（搜索、确认） | `bg-brand-blue`, `text-on-emphasis` 文字 |
| **Secondary** | 次要操作（取消、返回） | `bg-elevated`, 边框, 主色文字 |
| **Ghost** | 低优先级操作 | 透明背景, 悬停显示背景 |
| **Danger** | 危险操作（删除、清空） | `bg-danger/0.12`, 红色文字 |
| **Icon** | 纯图标按钮 | 44x44px 点击区域, 图标 22px |

### 按钮尺寸

| 尺寸 | 高度 | 水平内边距 | 字号 |
|------|------|-----------|------|
| Small | 28px | 10px | 11px |
| Medium (默认) | 32px | 16px | 12px |
| Large | 40px | 20px | 14px |

### 7.2 输入框 (Input)

- 高度: 32px (Small) / 40px (Medium)
- 边框: 1px `border-default`
- 聚焦: 边框 `border-active` + `box-shadow: 0 0 0 2px rgba(25,118,210,0.2)`
- 禁用: 背景 `bg-elevated`, 文字 `text-tertiary`
- 错误: 边框 `color-danger` + 下方错误文字

### 7.3 卡片 (Card)

所有卡片采用统一结构：

```
┌─ Card ──────────────────────────────────┐
│  Header: 标题 + 操作按钮                   │
│  ─────────────────────────────────────── │
│  Body: 内容区 (可滚动)                     │
│                                          │
└──────────────────────────────────────────┘
```

- 背景: `bg-surface`
- 边框: 1px `border-default`
- 圆角: `radius-lg` (8px)
- 内边距: Header `space-4` / Body 由内容决定

### 7.4 标签 (Tag/Badge)

| 变体 | 背景 | 文字色 |
|------|------|--------|
| Default | `bg-elevated` | `text-secondary` |
| Bullish | `rgba(244,67,54,0.12)` | `color-bullish` |
| Bearish | `rgba(76,175,80,0.12)` | `color-bearish` |
| Primary | `rgba(25,118,210,0.15)` | `brand-blue-light` |
| Warning | `rgba(245,158,11,0.12)` | `color-warning` |

### 7.5 表格 (Table)

金融数据表格规范：

- 表头: `text-xs`, `text-tertiary`, 大写, 加粗
- 数据行: `text-sm`, 交替行背景 (每 2 行微变)
- 数字列: 右对齐, `JetBrains Mono`
- 行高: 36px (紧凑) / 44px (舒适)
- 悬停行: `bg-elevated`
- 选中行: `bg-selected` + 左侧 3px `brand-blue` 指示条

### 7.6 状态组件

| 状态 | 组件 | 说明 |
|------|------|------|
| **加载中** | Skeleton | 灰色脉冲占位块，模拟内容形状 |
| **空数据** | EmptyState | 图标 + 标题 + 描述 + 行动按钮 |
| **错误** | ErrorState | 图标 + 错误信息 + 重试按钮 |
| **成功** | Toast | 右上角滑入通知，3秒自动消失 |

### 7.7 导航

```
┌──────┬──────────────────────────────────────────┐
│ Icon │  页面内容                                  │
│ Nav  │                                           │
│ 72px │                                           │
│      │                                           │
│ 🏠   │                                           │
│ ⭐   │                                           │
│ 🤖   │                                           │
│ 📊   │                                           │
│      │                                           │
│ ⚙️   │                                           │
└──────┴──────────────────────────────────────────┘
```

- 导航栏宽度: 72px（`NavRailWidth`，仅图标，实际实现值）
- 每个导航项: 44x44px 最小可点击区域（当前实现项宽 56 / 高 48，可进一步收敛）
- 活跃态: 左侧 3px 蓝色指示条 + 蓝色图标色
- 图标: 22px SVG（当前实现为 24px `DefaultIconSize`）

---

## 八、布局模式

### 8.1 首页 (Dashboard)

> v2.1 裁决：首页不放置 K 线区，采用 Bento Grid（热门标的置左最宽 + 快讯 + 最近查看）；K 线保留在资产详情页。下图保留原始三栏结构示意，仅作历史参考。

```
┌──────┬──────────────────────────────────┬──────────┐
│      │ TopBar (市场行情条)                │          │
│      ├──────────────────────────────────┤          │
│ Nav  │ Chart Section (K线图 + 指标)       │ Detail   │
│ 72px │                                  │ Panel    │
│      ├──────────────────────────────────┤ 320px    │
│      │ Metrics Grid (4列指标卡片)         │          │
│      ├──────────────────┬───────────────┤          │
│      │ Hot Assets       │ News Feed     │          │
│      │ (热门标的列表)     │ (7x24快讯)     │          │
│      └──────────────────┴───────────────┴──────────┘
└────────────────────────────────────────────────────┘
```

### 8.2 响应式断点

> v2.2 起与 v2.1 裁决统一为**两档**，废弃早期三档方案：

| 断点 | 宽度 | 布局变化 |
|------|------|---------|
| Full | >= 1100px | 完整布局（导航 + 内容 + 详情面板） |
| Compact | < 1100px | 单列布局，隐藏详情面板 |

- 主窗口最小宽度 800px（`MainWindowMinWidth`），Compact 档下指标卡片降为 2 列。

---

## 九、图标系统

### 替换 Emoji 方案（✅ v2.3 已落地）

6 个视图中的 emoji 已全部替换为 feather 风格 SVG 线性图标（24x24、stroke-width 2、语义配色），新增资源位于 `Assets/Images/`：

| 图标资源 | 语义 | 配色 | 替换位置 |
|----------|------|------|----------|
| `icon_ai.svg` | AI/芯片 | 品牌蓝 #42A5F5 | ProgressDisplayView、StrategyConfigView、AssetPageView |
| `icon_chart.svg` / `icon_clipboard.svg` | 评分/清单 | 品牌蓝 | AnalysisReportView 标题 |
| `icon_bulb.svg` / `icon_warning.svg` | 提示/警告 | 琥珀 #F59E0B | AnalysisReportView、ProgressDisplayView、AgentAnalysisPageView |
| `icon_danger.svg` | 危险警告 | 红 #EF4444 | AnalysisReportView（风险区块）、ApiKeyConfigView、AssetPageView |
| `icon_users.svg` / `icon_scale.svg` / `icon_edit.svg` / `icon_magnifier.svg` | 共识/分歧/综述/质量 | 蓝 / 琥珀 / 蓝 / 蓝 | AnalysisReportView 分析区块 |
| `icon_money.svg` / `icon_grid.svg` / `icon_shield.svg` / `icon_rocket.svg` / `icon_bolt.svg` | 定投/网格/保守/进取/快捷 | 绿 / 蓝 / 绿 / 橙 / 橙 | StrategyConfigView |
| `icon_refresh.svg` / `icon_document.svg` / `icon_chat.svg` | 刷新/导出/对话 | 蓝 / 蓝 / 白 | AssetPageView、AgentAnalysisPageView（FAB） |

> 说明：`✓` / `✕` 为普通字形（对勾/叉），非 emoji，予以保留。涨跌标签若需 12% 背景色块，直接引用 `BullishTagBackgroundBrush` / `BearishTagBackgroundBrush`。

### 图标规格

- 导航图标: 22x22px, stroke-width 1.5
- 按钮内图标: 16x16px, stroke-width 1.5
- 状态指示器: 8x8px 实心圆
- 文件格式: SVG (支持 Avalonia Svg.Skia 渲染)

---

## 十、迁移路线图（v2.2 状态盘点）

### P0 — 立即执行 (阻塞项)

1. ✅ **建立排版系统**: `TextStyles.axaml` 已定义 h1-h6 / body / caption / label / overline / data / 语义文本样式类（v2.4：旧样式兼容区已清理，无消费方的 `featureTitle` 删除、`feature-title` 统一收敛至 `CardStyles.axaml`）
2. ✅ **替换 Emoji 图标**: v2.3 已完成。6 个视图中的 emoji 已全部替换为 SVG 线性图标（清单见第九章）；`✓/✕` 为普通字形予以保留
3. ✅ **统一按钮变体**: `ButtonStyles.axaml` 已提供 Primary / Secondary / Ghost / Danger / 图标按钮变体

### P1 — 本迭代完成

4. ✅ **统一状态组件**: EmptyState / LoadingSkeleton（`SkeletonStyles.axaml`）已建立，ErrorState 复用程度待检查
5. ✅ **完善表单样式**: `FormStyles.axaml` 已统一样式
6. ✅ **完善列表样式**: `ListStyles.axaml` 已含选中/悬停/禁用态
7. ✅ **建立数据表格样式**: 行情类列表已覆盖；交易表格（`TradeHistoryView` 等）已核查——35 处设计 Token 引用、0 硬编码色，完全对齐

### P2 — 下个迭代

8. ✅ **Badge/Tag/Chip 组件**: 基础 Tag 样式已存在；涨跌标签背景画刷（v2.1 裁决第 4 条）已注册并落地渲染点（v2.3：收藏页涨跌幅标签改用 `ConverterParameter=tag` 12% 色块 + 同色文字，其余纯文本行情数字维持文字色方案）
9. ✅ **Toast 通知组件**: 按"或"裁决选择蓝调深色体系方案——`NotificationWindow` 专用色已迁入蓝调体系（`NotificationDark*` → `#161C2A/#1E2636/#20293D`），视觉与设计系统一致；嵌入式 Toast 形态降级为 P3 增强候选项
10. ✅ **响应式布局**: 两档断点（≥1100 完整布局 / <1100 单列隐藏详情面板）已实现，市场切换器随窗口自适应隐藏（8.2 节裁决即当前实现）

### P2+ — v2.2 登记（v2.3 落地状态）

11. ✅ **统一功能语义色双套并存**: `Success/Warning/Error/Info` 及 `*Hover/*Pressed` 已收敛至 Tailwind 系目标值，与 `BullishRed/BearishGreen` 的同值语义混叠已消除
12. ✅ **清理遗留脏色**: `RequiredFieldColor` / `DialogDangerBackground`（`#dc3545`）与 `PrimaryButtonColor`（`#007bff`）确认无消费方，已整体删除
13. ✅ **合并重复语义色组**: `WarningLight/InfoLight/SuccessLight/DangerLight` 等亮色硬编码组已整体删除（v2.3），消费方（`ButtonStyles` / `AdaptiveCardView` / `ApiKeyConfigView` / `AssetSelectionPageView`）全部改指主题感知的 `*Panel*` 组，暗色主题不再刺眼
14. ✅ **补齐缺失 Token**: `bg-selected`（暗/亮品牌蓝 15%）、涨跌标签背景画刷（含 `NeutralTagBackgroundBrush`）、`BorderActiveBrush`（暗 `#42A5F5` / 亮 `#1976D2`，`TextBox:focus` 与卡片 `:focus` 边框已接入）、遮罩 `DarkOverlayBrush`（`AgentAnalysisPageView` 已接入）全部补齐
15. ✅ **对齐亮色主题文本色**: `TextPrimary/Secondary/Disabled` 已对齐 `#1A1D26 / #6B7280 / #9CA3AF`
16. ✅ **主色按下态对齐**: 裁决采纳规范值，`PrimaryDark` 已定为 `#0D47A1`
17. ✅ **收敛字号/圆角阶梯**: 字号 15/20/24 → 14/18/22、圆角 10/16 → 8/12 已完成（`LargeCornerRadius` Token 删除，详见第三、五章注记）
18. ✅ **视图内散落样式收敛**: `ChatSidebarView` 本地 4 个 BoxShadow 与 `AgentAnalysisPageView` 侧栏阴影/硬编码遮罩已全部迁至全局 Token（新增 `SidebarBoxShadow` / `InputAreaBoxShadow` / `UserBubbleBoxShadow`，遮罩改用 `DarkOverlayBrush`）
19. ✅ **修复涨跌转换器硬编码色**（v2.3 新增）: `PriceChangeColorConverter` 原硬编码 Flat UI 色（`#e74c3c/#2ecc71/#6c757d`），已改为读取 `BullishBrush/BearishBrush/NeutralBrush` 资源并带兜底常量
20. ✅ **K 线图主题化**（v2.4 新增）: `kline_chart.html` 实现 `setTheme('dark'|'light')`（原注释声称可切换但函数从未实现，深色主题下 K 线区永久白底），宿主 `KLineChartView` 按 `ActualThemeVariant` 注入并监听联动；深色 palette 与兜底 HTML 配色（含 echarts loading 原 `#4d90fe/#000`）对齐设计系统
21. ✅ **on-color 文字 Token 收敛**（v2.4 新增）: 新增 `TextOnEmphasisBrush`（text-on-emphasis），全部视图/样式约 22 处 `Foreground="White"` 硬编码（含 btn-primary/btn-info 变体定义）收敛为 Token 引用
22. ✅ **动效时长收敛**（v2.4 新增）: 越阶梯的 0.2s×8、0.12s、0.8s 统一归入 150/600ms 阶梯（见第六章注记）
23. ✅ **排版兼容区清理**（v2.4 新增）: `TextStyles.axaml` 删除无消费方的 `featureTitle` 与重复定义的 `feature-title`

### P3 — 增强（功能储备，非偏差待办）

24. **键盘快捷键**: 专业用户常用的快捷键绑定（Ctrl+M 市场切换已实现）
25. **自定义主题色**: 允许用户微调品牌色
26. **窗口分屏**: 支持拖拽分屏查看多个标的
27. **嵌入式 Toast**: 以应用内嵌入式 Toast 形态替代独立 `NotificationWindow`（当前配色已对齐，形态升级为体验增强）

---

## 十一、实施现状速览（v2.3）

| 维度 | 状态 | 说明 |
|------|------|------|
| 深色背景四层体系 | ✅ 已落地 | `PageBackground #0A0E17` / `CardBackground #111722` / `Hover #161C2A` / `SurfaceVariant #1B2233`，与本文档一致 |
| 深色文本三级 | ✅ 已落地 | `#E4EAF5 / #8894A8 / #5A6680`，与本文档一致 |
| 红涨绿跌 | ✅ 已落地 | `BullishRed #F44336` / `BearishGreen #4CAF50`，与本文档一致 |
| 排版样式类 | ✅ 已落地 | `TextStyles.axaml` h1-h6 / body / data / caption / overline |
| 间距 4px 基准 | ✅ 已落地 | `Spacing.axaml`，字号/圆角/按钮高度 token 化 |
| 涨跌标签 12% 背景 | ✅ 已落地 | 画刷已注册（含 `NeutralTagBackgroundBrush`），收藏页涨跌幅标签已接入色块背景 |
| 功能语义色 | ✅ 已收敛 | Tailwind 系统一（v2.3），交互态同步重算；亮色硬编码组已删除，统一走 `*Panel*` 主题字典 |
| Emoji 图标 | ✅ 已替换 | 6 个视图 emoji 全部换为 SVG 线性图标（第九章清单） |
| 亮色主题文本/选中色 | ✅ 已对齐 | 文本 `#1A1D26/#6B7280/#9CA3AF`；选中态品牌蓝 15% 填充 |
| 通知窗口配色 | ✅ 已迁移 | `NotificationDark*` 已入蓝调深色体系（`#161C2A/#1E2636/#20293D`） |
| 涨跌数据着色 | ✅ 已修复 | `PriceChangeColorConverter` 改读设计系统资源（原硬编码 `#e74c3c/#2ecc71`） |
| 聚焦/遮罩 Token | ✅ 已补齐 | `BorderActiveBrush` 接入全部 `:focus` 边框；遮罩统一走 `DarkOverlayBrush` |
| 字号/圆角阶梯 | ✅ 已收敛 | 字号 11/12/13/14/16/18/22/28；圆角 4/6/8/12/28(FAB) |
| 阴影/散落样式 | ✅ 已收敛 | 全局 BoxShadow Token 统一供给，视图本地阴影定义清零 |
| K 线图主题 | ✅ 已落地（v2.4） | 内嵌页面 `setTheme` 接入 `ActualThemeVariant`，深色 palette 与兜底页配色对齐（原永久白底缺陷消除） |
| on-color 文字 | ✅ 已收敛（v2.4） | `TextOnEmphasisBrush` 全量替换 `Foreground="White"` 硬编码（约 22 处） |
| 动效时长 | ✅ 已收敛（v2.4） | 全部 Transition Duration 落在 150/300/600ms 阶梯 |
| 排版兼容区 | ✅ 已清理（v2.4） | `featureTitle` 与重复 `feature-title` 已删除 |

> 偏差明细与收敛计划统一登记在「十、迁移路线图」P2+ 小节，本表仅作快速索引。

---

## 附录: 与现有系统的对应关系

| 新设计系统 Token | 现有 Colors.axaml Key | 说明 |
|-----------------|----------------------|------|
| `color-bullish` | `BullishRed` (#F44336) | 保持一致 |
| `color-bearish` | `BearishGreen` (#4CAF50) | 保持一致 |
| `brand-blue` | `Primary` (#1976D2) | 保持一致 |
| `brand-accent` | `Accent` (#FF6B35) | 保持一致 |
| `brand-blue-dark` | `PrimaryDark` (#0D47A1) | ✅ 已对齐（v2.3 裁决采纳规范值） |
| `bg-surface` (dark) | `CardBackgroundBrush` (#111722) | ✅ 已对齐 |
| `bg-root` (dark) | `PageBackgroundBrush` (#0A0E17) | ✅ 已对齐 |
| `bg-elevated` (dark) | `HoverBackgroundBrush` (#161C2A) | ✅ 已对齐 |
| `text-primary` (dark) | `TextPrimaryBrush` (#E4EAF5) | ✅ 已对齐（减轻眩光） |
| `text-secondary` (dark) | `TextSecondaryBrush` (#8894A8) | ✅ 已对齐 |
| `bg-selected` (dark) | `SelectedBackgroundBrush` (#261976D2) | ✅ 已对齐（v2.3：品牌蓝 15% 填充，暗/亮统一） |
| `text-primary` (light) | `TextPrimaryBrush` (#1A1D26) | ✅ 已对齐（v2.3） |
| `text-secondary` (light) | `TextSecondaryBrush` (#6B7280) | ✅ 已对齐（v2.3） |
| `color-success` | `Success` (#10B981) | ✅ 已对齐（v2.3 收敛） |
| `color-warning` | `Warning` (#F59E0B) | ✅ 已对齐（v2.3 收敛） |
| `color-danger` | `Error` (#EF4444) | ✅ 已对齐（v2.3 收敛） |
| `color-info` | `Info` (#3B82F6) | ✅ 已对齐（v2.3 收敛） |
| `bg-overlay` (dark) | `SurfaceVariantBrush` (#1B2233) | ✅ 已对齐（v2.4 补记：变体面/遮罩底色，无独立 Token） |
| `text-on-emphasis` | `TextOnEmphasisBrush` (#FFFFFF) | ✅ 新增（v2.4：明暗主题同值的 on-color 文字） |
