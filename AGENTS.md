# AGENTS.md

面向代码智能体（Agents）的专用说明文件。本项目为跨平台 .NET MAUI 应用，包含 Windows（WinUI）与 Mac Catalyst 头项目，以及独立的测试工程。智能体在执行改动前后应据此文件自动执行构建与测试命令，确保提交前处于可运行、测试通过的状态。

---

## 一、项目概览

- 解决方案：`MarketAssistant.slnx`
- 共享 MAUI 项目：`MarketAssistant/MarketAssistant/`
- Windows 头项目（WinUI）：`MarketAssistant/MarketAssistant.WinUI/`
- Mac 头项目（Mac Catalyst）：`MarketAssistant/MarketAssistant.Mac/`
- 单元测试工程：`TestMarketAssistant/`
- 构建脚本：根目录 `build-release.ps1`，构建说明见根目录 `BUILD.md`

主要功能模块：

- 业务与设置：`MarketAssistant/MarketAssistant/Applications/`
- 视图与视图模型：`MarketAssistant/MarketAssistant/Pages/`, `MarketAssistant/MarketAssistant/ViewModels/`
- 插件能力（基础/财务/新闻/技术/筛选）：`MarketAssistant/MarketAssistant/Plugins/`
- 代理与多分析角色：`MarketAssistant/MarketAssistant/Agents/` 及 `Agents/Yaml`
- 资源与样式：`MarketAssistant/MarketAssistant/Resources/`
- 模型配置：`MarketAssistant/MarketAssistant/config/models.yaml`

---

## 二、开发环境与准备

### 1. 必备工具

- .NET SDK 8.0（或以上）
- 对应工作负载（按平台择一或全部安装）

```bash
dotnet --info
dotnet workload install maui-windows
dotnet workload install maui-maccatalyst
```

可选：安装 Playwright CLI（用于首次拉起浏览器依赖）

```bash
dotnet tool update --global Microsoft.Playwright.CLI
playwright install
```

### 2. 还原与编译

```bash
dotnet restore MarketAssistant.slnx
dotnet build MarketAssistant.slnx -c Debug
```

### 3. 运行（开发）

Windows（WinUI 头项目）：

```bash
dotnet run --project MarketAssistant/MarketAssistant.WinUI/MarketAssistant.WinUI.csproj -c Debug
```

macOS（Mac Catalyst 头项目）：

```bash
dotnet run --project MarketAssistant/MarketAssistant.Mac/MarketAssistant.Mac.csproj -c Debug
```

---

## 三、测试与质量检查

### 1. 运行全部测试

```bash
dotnet test TestMarketAssistant/TestMarketAssistant.csproj -c Debug --logger "trx;LogFileName=TestResults.trx"
```

### 2. 按名称过滤运行

```bash
dotnet test TestMarketAssistant/TestMarketAssistant.csproj --filter FullyQualifiedName~StockServiceTest
```

### 3. 代码格式（若需）

```bash
dotnet format --verify-no-changes
# 如需自动修复：
dotnet format
```

智能体在完成代码编辑后，应自动执行：还原 → 构建 → 测试 全流程，并在失败时回滚或继续修复直至通过为止。

---

## 四、配置与运行时约定

- 模型与供应商配置：`MarketAssistant/MarketAssistant/config/models.yaml`
- MCP 相关设置与页面：`Applications/Settings/MCPServerConfig*.cs` 与 `Pages/MCPServerConfigPage.xaml`
- 插件的 YAML 能力描述：`MarketAssistant/MarketAssistant/Plugins/Yaml/`

建议：

- 不要在仓库中提交任何密钥或令牌。密钥应通过应用内设置页或安全存储注入。
- 如引入新外部依赖，需在 README 或本文件中注明安装步骤与运行前置条件。

---

## 五、代码风格与工程约束

通用（C# 12 / .NET 8）：

- 仅在函数级添加文档注释；仅对晦涩逻辑添加必要行上方说明，不写赘余注释。
- 命名清晰、可读；优先完整词汇，避免缩写；异步方法以 `Async` 结尾。
- 控制流优先使用早返回与卫语句，避免深层嵌套；不要吞并异常。
- 避免 `TODO` 留存，能实现则实现；公共 API 明确类型标注。
- 变更应保持现有依赖注入与分层结构（`ViewModels`、`Applications`、`Services`、`Plugins` 等）。

UI 与样式：

- XAML/样式中的 `Padding`/`Margin`/间距使用 4 的倍数（4/8/12/16），且不超过 16（来源：团队偏好）。
- 统一遵循现有 `Resources/Styles/` 配置，避免在视图中硬编码颜色与字体。

> 说明：以上 UI 间距约束来自项目偏好设置 [[memory:4590929]]。

---

## 六、目录导航（常用）

- 代理与分析角色：`MarketAssistant/MarketAssistant/Agents/`（含多角色 YAML 配置）
- 业务能力：`MarketAssistant/MarketAssistant/Applications/`（股票、资讯、收藏、K 线等）
- 插件能力：`MarketAssistant/MarketAssistant/Plugins/`（基础/财务/新闻/技术/筛选；含模型与解析 YAML）
- 视图与 VM：`MarketAssistant/MarketAssistant/Pages/`, `MarketAssistant/MarketAssistant/ViewModels/`
- 资源与样式：`MarketAssistant/MarketAssistant/Resources/`（含 `AnalysisReport.liquid`, `kline_chart.html`）
- 测试：`TestMarketAssistant/`

---

## 七、发布与打包

Windows 一键发布脚本（PowerShell）：

```powershell
powershell -ExecutionPolicy Bypass -File .\build-release.ps1
```

更详细的发布与生成说明请参阅根目录 `BUILD.md`。如需调整应用清单与打包参数，请查看 `MarketAssistant/MarketAssistant.WinUI/Package.appxmanifest`。

---

## 八、对智能体的补充指令

- 在提交前总是执行：`dotnet restore` → `dotnet build` → `dotnet test`。
- 修改或新增插件时：
  - 在 `Plugins/` 下新增实现，同时在 `TestMarketAssistant/` 添加或更新对应测试。
  - 如引入新配置项，更新 `config/models.yaml` 示例并在本文件“配置与运行时约定”处补充说明。
- 涉及 UI 的改动，遵循“4 的倍数、不超过 16”的间距规范，并尽量通过样式资源集中管理。
- 非必要不改动图片与资产文件；若必须更改，请确保最终构建体积可控。

---

## 九、PR 与提交规范

- 提交信息建议格式：`[模块] 变更概要`，例如：`[Plugins] 新增资金流插件与测试`。
- 所有提交需通过本地测试；如涉及平台相关改动，至少在一个目标平台（Windows 或 macOS）完成启动验证。
- 对基础设施或脚手架的新增，请在 `README.md` 或本文件补充对应说明与命令。

---

## 十、常见问题（FAQ）

- Q：测试或类型检查是否为必需？
  - A：是。提交前需保证 `dotnet build` 与 `dotnet test` 全部通过。
- Q：智能体会自动执行本文件中的命令吗？
  - A：会。智能体应基于本文件的“构建与测试”指令自动运行并在失败时尝试修复。
- Q：是否可为子目录添加更细化的 AGENTS.md？
  - A：可以。若在子项目放置更近的 `AGENTS.md`，就近原则生效。



