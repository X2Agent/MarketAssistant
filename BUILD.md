# MarketAssistant 构建和发布指南

本文档介绍如何为 MarketAssistant 项目构建和发布跨平台版本。本项目基于 Avalonia UI 开发，支持 Windows、macOS 和 Linux。

## 项目结构

- `src/` - Avalonia 主项目
- `tests/` - 单元测试项目
- `MarketAssistant.slnx` - 解决方案文件

## 自动化发布 (GitHub Actions)

### 设置

1. 确保你的代码已推送到 GitHub 仓库
2. GitHub Actions 工作流文件已创建在 `.github/workflows/release.yml`

### 触发发布

#### 方法 1: 创建 Release (推荐)

1. 在 GitHub 仓库页面，点击 "Releases"
2. 点击 "Create a new release"
3. 创建新的标签 (例如: `v1.0.0`)
4. 填写发布说明
5. 点击 "Publish release"

发布后，GitHub Actions 将自动:
- 构建 Windows x64 版本
- 构建 macOS 版本
- 创建安装包 (ZIP/DMG)
- 将文件附加到 Release 中

#### 方法 2: 手动触发

1. 在 GitHub 仓库页面，点击 "Actions"
2. 选择 "Build and Release" 工作流
3. 点击 "Run workflow"
4. 选择分支并点击 "Run workflow"

### 构建产物

- `MarketAssistant-Windows-x64.zip` - Windows 版本 (包含 exe 和依赖文件)
- `MarketAssistant-macOS.dmg` - macOS 磁盘映像文件
- `MarketAssistant-Linux-x64.zip` - Linux 版本

## 本地构建

### 前置要求

- .NET 9.0 SDK
- 无需额外工作负载（Avalonia 通过 NuGet 包提供所有依赖）

### 使用 PowerShell 脚本 (推荐)

```powershell
# Windows 一键发布
.\build-release.ps1
```

### 手动构建

#### Windows 版本

```bash
# 还原依赖项
dotnet restore src/MarketAssistant.csproj

# 构建并发布 Windows 版本
dotnet publish src/MarketAssistant.csproj \
  -c Release \
  -r win-x64 \
  --self-contained \
  -o ./publish/windows
```

#### macOS 版本

```bash
# 还原依赖项
dotnet restore src/MarketAssistant.csproj

# 构建并发布 macOS 版本
dotnet publish src/MarketAssistant.csproj \
  -c Release \
  -r osx-x64 \
  --self-contained \
  -o ./publish/macos
```

#### Linux 版本

```bash
# 还原依赖项
dotnet restore src/MarketAssistant.csproj

# 构建并发布 Linux 版本
dotnet publish src/MarketAssistant.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained \
  -o ./publish/linux
```

## 发布配置说明

### 通用配置

- **目标框架**: `net9.0`
- **UI 框架**: Avalonia UI 11.3.7
- **发布模式**: 自包含 (self-contained)

### 平台特定配置

#### Windows
- **运行时标识符**: win-x64
- **输出**: 可执行文件 (.exe)

#### macOS
- **运行时标识符**: osx-x64
- **输出**: 可执行文件

#### Linux
- **运行时标识符**: linux-x64
- **输出**: 可执行文件

## 故障排除

### 常见问题

1. **构建失败 - 缺少依赖项**
   ```bash
   dotnet restore MarketAssistant.slnx
   dotnet clean MarketAssistant.slnx
   dotnet restore src/MarketAssistant.csproj
   ```

2. **Playwright 浏览器未安装**
   ```bash
   dotnet tool update --global Microsoft.Playwright.CLI
   playwright install
   ```

3. **跨平台构建注意事项**
   - Windows/Linux 版本可以在任何平台上构建
   - macOS 版本建议在 macOS 上构建以获得最佳兼容性
   - 发布时使用 `--self-contained` 以包含所有运行时依赖

### 日志和调试

- GitHub Actions 日志可在 Actions 页面查看
- 本地构建错误会显示在控制台
- 使用 `-v detailed` 参数获取详细构建信息

## 代码签名 (可选)

### Windows 代码签名

如需对 Windows 版本进行代码签名，需要:

1. 获取代码签名证书
2. 在 GitHub Secrets 中添加证书信息
3. 修改 GitHub Actions 工作流添加签名步骤

### macOS 代码签名

如需对 macOS 版本进行代码签名和公证，需要:

1. Apple Developer 账户
2. 开发者证书
3. 在 GitHub Secrets 中添加证书和密码
4. 修改工作流添加签名和公证步骤

## 更新版本号

在发布新版本前，记得更新:

1. 项目文件中的版本号
2. AssemblyInfo 文件
3. 应用清单文件
4. README 和文档

---

## 相关文档

- [Avalonia UI 官方文档](https://docs.avaloniaui.net/)
- [.NET 9.0 发布指南](https://docs.microsoft.com/dotnet/core/deploying/)
- [MAUI 到 Avalonia 迁移指南](MAUI到Avalonia迁移指南.md)