# MarketAssistant 构建和发布指南

本文档介绍如何为 MarketAssistant 项目构建和发布 Windows exe 和 macOS dmg 文件。

## 项目结构

- `MarketAssistant/MarketAssistant/` - 核心 MAUI 项目
- `MarketAssistant/MarketAssistant.WinUI/` - Windows 特定项目
- `MarketAssistant/MarketAssistant.Mac/` - macOS 特定项目

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
- `MarketAssistant-macOS.zip` - macOS 备用 ZIP 文件

## 本地构建

### 前置要求

- .NET 9.0 SDK
- Visual Studio 2022 (Windows) 或 Visual Studio for Mac (macOS)
- MAUI 工作负载

### 使用 PowerShell 脚本 (推荐)

```powershell
# 构建所有平台
.\build-release.ps1

# 仅构建 Windows 版本
.\build-release.ps1 -Platform Windows

# 仅构建 macOS 版本 (需要在 macOS 上运行)
.\build-release.ps1 -Platform macOS
```

### 手动构建

#### Windows 版本

```bash
# 安装 MAUI 工作负载
dotnet workload install maui

# 还原依赖项
dotnet restore MarketAssistant.sln

# 构建 Windows 版本
dotnet publish MarketAssistant/MarketAssistant.WinUI/MarketAssistant.WinUI.csproj \
  -c Release \
  -f net9.0-windows10.0.19041.0 \
  -p:Platform=x64 \
  -p:PublishSingleFile=true \
  -p:SelfContained=true \
  -p:RuntimeIdentifier=win-x64 \
  -o ./publish/windows
```

#### macOS 版本 (仅在 macOS 上)

```bash
# 安装 MAUI 工作负载
dotnet workload install maui

# 还原依赖项
dotnet restore MarketAssistant.sln

# 构建 macOS 版本
dotnet publish MarketAssistant/MarketAssistant.Mac/MarketAssistant.Mac.csproj \
  -c Release \
  -f net9.0-maccatalyst \
  -p:CreatePackage=true \
  -o ./publish/macos

# 创建 DMG (可选)
hdiutil create -volname "MarketAssistant" \
  -srcfolder ./publish/macos/MarketAssistant.app \
  -ov -format UDZO \
  ./MarketAssistant-macOS.dmg
```

## 发布配置说明

### Windows 配置

- **目标框架**: `net9.0-windows10.0.19041.0`
- **平台**: x64
- **发布模式**: 单文件 + 自包含
- **运行时**: win-x64

### macOS 配置

- **目标框架**: `net9.0-maccatalyst`
- **最低支持版本**: macOS 15.0
- **输出**: .app 应用包
- **分发**: DMG 磁盘映像

## 故障排除

### 常见问题

1. **MAUI 工作负载未安装**
   ```bash
   dotnet workload install maui
   ```

2. **构建失败 - 缺少依赖项**
   ```bash
   dotnet restore MarketAssistant.sln
   dotnet clean MarketAssistant.sln
   ```

3. **macOS 构建失败**
   - 确保在 macOS 系统上构建
   - 检查 Xcode 是否已安装
   - 验证开发者证书配置

4. **Windows 构建失败**
   - 确保安装了 Windows SDK
   - 检查 Visual Studio 工作负载

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

有关更多信息，请参考 [.NET MAUI 官方文档](https://docs.microsoft.com/dotnet/maui/)。