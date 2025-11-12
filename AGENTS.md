# AGENTS.md

面向代码智能体（Agents）的专用说明文件。本项目为基于 Avalonia UI 的跨平台桌面应用，支持 Windows、macOS 和 Linux 平台，以及独立的测试工程。智能体应根据改动类型选择合适的验证方式，确保提交前代码可正常构建。

---

## 一、项目概览

- 解决方案：`MarketAssistant.slnx`
- 主项目（Avalonia 应用）：`src/`
- 单元测试工程：`tests/`
- 构建脚本：根目录 `build-release.ps1`，构建说明见根目录 `BUILD.md`

主要功能模块：

- 业务与设置：`src/Applications/`
- 视图与视图模型：`src/Views/`, `src/ViewModels/`
- 插件能力（基础/财务/新闻/技术/筛选）：`src/Agents/Plugins/`
- 代理与多分析角色：`src/Agents/` 及 `Agents/Yaml`
- 资源与样式：`src/Resources/Styles/`
- 资产文件：`src/Assets/`
- 模型配置：`src/config/models.yaml`

---

## 二、开发环境与准备

### 1. 必备工具

- .NET SDK 10.0（或以上）
- 无需额外工作负载，Avalonia 通过 NuGet 包提供

```bash
dotnet --info
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

或针对主项目：

```bash
dotnet restore src/MarketAssistant.csproj
dotnet build src/MarketAssistant.csproj -c Debug
```

### 3. 运行（开发）

跨平台运行（Windows/macOS/Linux）：

```bash
dotnet run --project src/MarketAssistant.csproj -c Debug
```

---

## 三、测试与质量检查

### 1. 运行全部测试

```bash
dotnet test tests/TestMarketAssistant.csproj -c Debug --logger "trx;LogFileName=TestResults.trx"
```

### 2. 按名称过滤运行

```bash
dotnet test tests/TestMarketAssistant.csproj --filter FullyQualifiedName~StockServiceTest
```

### 3. 代码格式（若需）

```bash
dotnet format --verify-no-changes
# 如需自动修复：
dotnet format
```

智能体在完成代码编辑后的验证策略：

- **核心代码改动**（Services、Agents、Plugins、Applications 业务逻辑）：执行 `dotnet build` 验证编译通过，测试为可选项
- **UI 改动**（Views、样式、资源）：执行 `dotnet build` 验证编译通过
- **文档改动**（README、AGENTS.md 等）：无需执行构建或测试
- **配置改动**（YAML、设置）：执行 `dotnet build` 验证编译通过

如构建失败，应尝试修复直至通过。单元测试仅在用户明确要求或重大重构时执行。

---

## 四、配置与运行时约定

- 模型与供应商配置：`src/config/models.yaml`
- MCP 相关设置：`src/Applications/Settings/MCPServerConfig*.cs`
- MCP 配置页面：`src/Views/Pages/MCPServerConfigPageView.axaml`
- 插件的 YAML 能力描述：`src/Agents/Plugins/Yaml/`
- 代理 YAML 配置：`src/Agents/Yaml/`

建议：

- 不要在仓库中提交任何密钥或令牌。密钥应通过应用内设置页或安全存储注入。
- 如引入新外部依赖，需在 README 或本文件中注明安装步骤与运行前置条件。

---

## 五、代码风格与工程约束

通用（C# 13 / .NET 10）：

- 仅在函数级添加文档注释；仅对晦涩逻辑添加必要行上方说明，不写赘余注释。
- 命名清晰、可读；优先完整词汇，避免缩写；异步方法以 `Async` 结尾。
- 控制流优先使用早返回与卫语句，避免深层嵌套；不要吞并异常。
- 避免 `TODO` 留存，能实现则实现；公共 API 明确类型标注。
- 变更应保持现有依赖注入与分层结构（`ViewModels`、`Applications`、`Services`、`Agents` 等）。

UI 与样式（Avalonia AXAML）：

- AXAML/样式中的 `Padding`/`Margin`/间距使用 4 的倍数（4/8/12/16），且不超过 16（来源：团队偏好）。
- 统一遵循现有 `src/Resources/Styles/` 配置，避免在视图中硬编码颜色与字体。
- 视图文件使用 `.axaml` 扩展名（Avalonia XAML）。

> 说明：以上 UI 间距约束来自项目偏好设置 [[memory:4590929]]。

---

## 六、目录导航（常用）

- 代理与分析角色：`src/Agents/`（含多角色 YAML 配置 `Yaml/`）
- 业务能力：`src/Applications/`（股票、资讯、收藏、K 线、设置等）
- 插件能力：`src/Agents/Plugins/`（基础/财务/新闻/技术/筛选；含 YAML 描述文件）
- 视图与 VM：`src/Views/`, `src/ViewModels/`
- 资源与样式：`src/Resources/Styles/`（Avalonia 样式资源字典）
- 资产文件：`src/Assets/`（图片、图标、HTML 等）
- 类型转换器：`src/Converts/`
- 基础设施：`src/Infrastructure/`（配置、核心、工厂等）
- 服务层：`src/Services/`（浏览器、缓存、对话框、导航等）
- RAG 相关：`src/Rag/`（向量化与检索增强生成）
- 测试：`tests/`

---

## 七、发布与打包

### Windows 发布

使用一键发布脚本（PowerShell）：

```powershell
powershell -ExecutionPolicy Bypass -File .\build-release.ps1
```

### 跨平台发布

手动发布到特定平台：

```bash
# Windows
dotnet publish src/MarketAssistant.csproj -c Release -r win-x64 --self-contained

# macOS
dotnet publish src/MarketAssistant.csproj -c Release -r osx-x64 --self-contained

# Linux
dotnet publish src/MarketAssistant.csproj -c Release -r linux-x64 --self-contained
```

更详细的发布与生成说明请参阅根目录 `BUILD.md`。

---

## 八、对智能体的补充指令

### 验证策略

根据改动类型选择合适的验证方式：

#### 所有代码改动（必需）
- 修改或新增插件（`src/Agents/Plugins/`）
- 修改业务逻辑（`src/Applications/`、`src/Services/`）
- 修改 Agent 核心代码（`src/Agents/`）
- 修改基础设施（`src/Infrastructure/`、`src/Rag/`）
- 修改数据模型或解析器（`src/Models/`、`src/Parsers/`）
- 修改 UI 视图（`src/Views/`）
- 修改 ViewModel（`src/ViewModels/`）
- 修改样式资源（`src/Resources/Styles/`）
- 修改转换器（`src/Converts/`）
- 修改配置文件（`src/config/`）

**验证命令**：`dotnet build`（确保编译通过）

#### 单元测试（可选）
单元测试仅在以下情况执行：
- 用户明确要求执行测试
- 进行重大架构重构
- 修复已知的测试失败问题

**测试命令**：`dotnet test`（可针对相关测试文件过滤执行）

#### 无需验证的场景
- 修改文档（README.md、AGENTS.md、BUILD.md 等）
- 修改注释
- 修改资产文件（图片、图标等）

### 其他开发指令

- 修改或新增插件时：
  - 在 `src/Agents/Plugins/` 下新增实现，确保代码编译通过。
  - 同时在 `tests/` 添加或更新对应测试（但不需要自动执行测试）。
  - 如引入新配置项，更新 `src/config/models.yaml` 示例并在本文件"配置与运行时约定"处补充说明。
- 涉及 UI 的改动：
  - 遵循"4 的倍数、不超过 16"的间距规范。
  - 尽量通过 `src/Resources/Styles/` 中的样式资源集中管理。
  - 使用 `.axaml` 文件扩展名（Avalonia XAML）。
  - 优先使用 Avalonia 内置控件，必要时参考现有自定义控件实现。
- 非必要不改动图片与资产文件；若必须更改，请确保最终构建体积可控。

---

## 九、PR 与提交规范

- 提交信息建议格式：`[模块] 变更概要`，例如：`[Plugins] 新增资金流插件与测试`。
- 所有代码改动需确保构建通过；单元测试为可选，由开发者根据实际情况决定是否执行。
- 如涉及平台相关改动，至少在一个目标平台（Windows 或 macOS）完成启动验证。
- 对基础设施或脚手架的新增，请在 `README.md` 或本文件补充对应说明与命令。

---

## 十、常见问题（FAQ）

- Q：测试是否为必需？
  - A：不是必需的。智能体默认只确保代码编译通过，测试为可选项，仅在用户明确要求或重大重构时执行。
- Q：智能体会自动执行测试吗？
  - A：不会。智能体只会执行 `dotnet build` 确保编译通过，不会自动执行单元测试，除非用户明确要求。
- Q：如何运行特定模块的测试？
  - A：使用过滤器，例如：`dotnet test --filter FullyQualifiedName~StockServiceTest`
- Q：是否可为子目录添加更细化的 AGENTS.md？
  - A：可以。若在子项目放置更近的 `AGENTS.md`，就近原则生效。



