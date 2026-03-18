# MarketAssistant.App — AGENTS.md

主应用入口项目（Avalonia 桌面应用）。**职责限定为 UI 宿主**：包含 View、ViewModel、导航、通知、对话框、样式资源和内容文件输出；业务逻辑、Agent 实现、Workflow、Tool、MCP、交易引擎收敛在 `MarketAssistant.App.Services`，RAG 基础能力收敛在 `MarketAssistant.Rag`。

---

## 目录结构

```
MarketAssistant.App/
├── config/
│   ├── models.yaml                ← AI 模型与供应商配置
│   └── prompts/analysts.yaml      ← 分析师提示词配置
├── Converts/                      ← AXAML 值转换器
├── Infrastructure/                ← ViewLocator 等 UI 宿主基础设施
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
  - `Padding`/`Margin`/间距使用 **4 的倍数**（4/8/12/16），原则上不超过 16。
  - **禁止硬编码数值**：使用 `Resources/Styles/Spacing.axaml` 中的资源（如 `{StaticResource SmallMargin}`），或在 `UserControl.Resources` 中定义局部资源。
- **样式管理**：统一遵循 `Resources/Styles/` 中的集中式样式资源，避免硬编码颜色与字体。
- **资源引用**：
  - `Colors.axaml` 中 `Primary`、`PrimaryLight` 等是 **Color** 类型，`Background`/`Foreground` 必须使用对应 **Brush**（`PrimaryBrush`、`PrimaryLightBrush`）。
  - 主题感知资源 → `{DynamicResource}`；固定资源 → `{StaticResource}`。
- **控件**：优先使用 Avalonia 内置控件。
- **资产**：非必要不改动图片与资产文件。

---

## ViewModel 编码规范

- 使用 `CommunityToolkit.Mvvm` 源生成器：`[ObservableProperty]`、`[RelayCommand]`。
- `ICommand` 属性禁止在 getter 中每次创建新实例。
- 订阅外部事件的 ViewModel 实现 `IDisposable`，在 `Dispose` 中取消订阅。
- `ObservableCollection` 在构造函数中初始化一次。
- 异常处理：使用 `SafeExecuteAsync` 或 `ErrorMessageMapper`，**不要吞并异常**。
- 通过 DI 容器获取依赖，不使用 `ServiceLocator`。
- ViewModel 可以消费 `MarketAssistant.App.Services` 暴露的业务服务，但不要在 UI 层新增 Agent、Tool、Workflow 实现。

---

## 边界约定

- `MarketAssistant.App` 不再承载 Agent Tool、Workflow、RAG、交易引擎等运行时代码。
- 新增 Agent/Tool/Workflow/业务服务时，放到 `MarketAssistant.App.Services`；新增文档解析、向量化、检索、重排等 RAG 基础能力时，放到 `MarketAssistant.Rag`；本项目只保留 UI 相关适配。
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

- `config/prompts/analysts.yaml`：分析师提示词配置，每个分析师含 `name`、`displayName`、`temperature`、`topP`、`topK`、`instructions` 字段，运行时热加载。
- `config/models.yaml`：AI 模型与供应商配置。

---

## 构建与运行

```bash
# 构建
dotnet build src/MarketAssistant.App/MarketAssistant.App.csproj -c Debug

# 运行
dotnet run --project src/MarketAssistant.App/MarketAssistant.App.csproj -c Debug
```

可选：安装 Playwright CLI（业务运行时中的浏览器自动化依赖会使用）：

```bash
dotnet tool update --global Microsoft.Playwright.CLI
playwright install
```
