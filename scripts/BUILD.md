# MarketAssistant 构建和发布指南

本文档介绍如何为 MarketAssistant 项目构建和发布跨平台版本。本项目基于 Avalonia UI 开发，支持 Windows、macOS 和 Linux，采用 Avalonia 官方推荐的部署方案。

## 📚 目录

- [项目结构](#项目结构)
- [前置要求](#前置要求)
- [快速开始](#快速开始)
- [平台特定构建](#平台特定构建)
  - [Windows](#windows-构建)
  - [macOS](#macos-构建)
  - [Linux](#linux-构建)
- [自动化发布](#自动化发布-github-actions)
- [代码签名](#代码签名)
- [故障排除](#故障排除)

---

## 项目结构

```
MarketAssistant/
├── src/                        # Avalonia 主项目
├── tests/                      # 单元测试项目
├── scripts/                    # 构建脚本
│   ├── build-release.ps1      # 主构建脚本（跨平台）
│   ├── macos/                 # macOS 特定构建资源
│   │   ├── build-app-bundle.sh
│   │   ├── Info.plist.template
│   │   └── MarketAssistant.entitlements
│   └── linux/                 # Linux 特定构建资源
│       ├── build-deb.sh
│       ├── build-rpm.sh
│       └── marketassistant.desktop
├── .github/workflows/         # GitHub Actions 工作流
└── MarketAssistant.slnx       # 解决方案文件
```

---

## 前置要求

### 所有平台

- **.NET 9.0 SDK** 或更高版本
- 无需额外工作负载（Avalonia 通过 NuGet 包提供所有依赖）

```bash
dotnet --version
```

### 平台特定工具

#### macOS
- **Xcode Command Line Tools**（用于代码签名和公证）
  ```bash
  xcode-select --install
  ```

#### Linux
- **dpkg-deb**（用于创建 .deb 包，Ubuntu/Debian 默认包含）
- **rpmbuild**（可选，用于创建 .rpm 包）
  ```bash
  # Ubuntu/Debian
  sudo apt-get install rpm
  
  # Fedora/RHEL
  sudo dnf install rpm-build
  ```

---

## 快速开始

### 使用主构建脚本（推荐）

```powershell
# Windows - 构建当前平台
.\scripts\build-release.ps1

# macOS/Linux - 构建当前平台
pwsh scripts/build-release.ps1 -Platform macOS  # 或 Linux
```

### 构建所有平台

```powershell
.\scripts\build-release.ps1 -Platform All
```

> **注意**：在 Windows 上构建 macOS/Linux 时，不会创建平台特定的安装包（.app/.dmg/.deb），只会生成 ZIP 归档。要创建完整的平台特定包，请在对应平台上构建。

---

## 平台特定构建

### Windows 构建

#### 使用脚本

```powershell
.\scripts\build-release.ps1 -Platform Windows
```

#### 手动构建

```bash
# 发布
dotnet publish src/MarketAssistant.csproj \
  -c Release \
  -r win-x64 \
  --self-contained \
  -p:PublishReadyToRun=true \
  -p:UseAppHost=true \
  -o ./publish/windows

# 打包
Compress-Archive -Path ./publish/windows/* -DestinationPath MarketAssistant-Windows-x64.zip
```

#### 输出

- `MarketAssistant-Windows-x64.zip` - 包含 .exe 和所有依赖文件

---

### macOS 构建

macOS 构建遵循 [Avalonia 官方 macOS 部署指南](https://docs.avaloniaui.net/docs/deployment/macOS)，创建标准的 `.app` bundle 和 `.dmg` 磁盘映像。

#### 使用脚本（推荐）

```bash
# 在 macOS 上运行
chmod +x ./scripts/macos/build-app-bundle.sh
./scripts/macos/build-app-bundle.sh
```

或使用主构建脚本：

```bash
pwsh scripts/build-release.ps1 -Platform macOS
```

#### 配置文件

- **Info.plist** - 应用程序元数据
  - CFBundleIdentifier: `com.marketassistant.app`
  - CFBundleName: `MarketAssistant`
  - CFBundleVersion: 从 `.csproj` 读取
  
- **Entitlements** - 应用权限
  - 网络访问（客户端/服务器）
  - 文件系统访问
  - JIT 编译支持

#### 代码签名（可选）

```bash
# 设置签名标识
export SIGNING_IDENTITY="Your Developer ID Application"

# 运行构建（将自动签名）
./scripts/macos/build-app-bundle.sh
```

#### 公证（可选）

```bash
# 配置公证工具
export NOTARYTOOL_PROFILE="AC_PASSWORD"

# 运行构建（将自动公证）
./scripts/macos/build-app-bundle.sh
```

#### 输出

- `Release/macOS/MarketAssistant.app` - 应用程序 bundle
- `Release/macOS/MarketAssistant-1.0.0.dmg` - DMG 磁盘映像

#### 验证

```bash
# 验证 .app bundle 结构
ls -la Release/macOS/MarketAssistant.app/Contents/

# 验证代码签名
codesign --verify --deep --strict --verbose=2 Release/macOS/MarketAssistant.app

# 测试运行
open Release/macOS/MarketAssistant.app
```

---

### Linux 构建

Linux 构建遵循 [Avalonia Debian/Ubuntu 打包指南](https://docs.avaloniaui.net/docs/deployment/debian-ubuntu)，创建标准的 `.deb` 和 `.rpm` 安装包。

#### Debian/Ubuntu (.deb)

```bash
# 在 Linux 上运行
chmod +x ./scripts/linux/build-deb.sh
./scripts/linux/build-deb.sh
```

**输出：**
- `Release/Linux/MarketAssistant_1.0.0_amd64.deb` - Debian 安装包
- `Release/Linux/MarketAssistant-Linux-x64.zip` - ZIP 归档

**安装：**
```bash
sudo dpkg -i Release/Linux/MarketAssistant_1.0.0_amd64.deb
```

**卸载：**
```bash
sudo apt remove marketassistant
```

#### Fedora/RHEL/CentOS (.rpm)

```bash
# 安装 rpmbuild
sudo dnf install rpm-build

# 构建
chmod +x ./scripts/linux/build-rpm.sh
./scripts/linux/build-rpm.sh
```

**输出：**
- `Release/Linux/MarketAssistant-1.0.0-x86_64.rpm` - RPM 安装包

**安装：**
```bash
sudo rpm -ivh Release/Linux/MarketAssistant-1.0.0-x86_64.rpm
```

#### 桌面集成

构建后自动创建：
- **Desktop 文件** - `/usr/share/applications/marketassistant.desktop`
- **图标** - `/usr/share/icons/hicolor/256x256/apps/marketassistant.png`
- **符号链接** - `/usr/bin/marketassistant` → `/opt/MarketAssistant/MarketAssistant`

#### 手动构建（跨平台兼容）

```bash
# 在任何平台上
dotnet publish src/MarketAssistant.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained \
  -p:PublishReadyToRun=true \
  -p:UseAppHost=true \
  -o ./publish/linux

# 打包
zip -r MarketAssistant-Linux-x64.zip ./publish/linux/
```

---

## 自动化发布 (GitHub Actions)

本项目配置了两个 GitHub Actions 工作流：

### 1. 持续集成 (build.yml)

在每次 push 或 PR 时运行，验证代码可以在所有平台上构建。

```yaml
触发条件：
  - Push to: main, develop, feat/*
  - Pull Request to: main, develop

构建平台：
  - Windows (windows-latest)
  - macOS (macos-latest)
  - Linux (ubuntu-latest)
```

### 2. 发布构建 (release.yml)

创建完整的发布包，包含所有平台的安装程序。

#### 触发发布

**方法 1：创建 Release（推荐）**

1. 在 GitHub 仓库页面，点击 "Releases"
2. 点击 "Create a new release"
3. 创建新的标签（例如：`v1.0.0`）
4. 填写发布说明
5. 点击 "Publish release"

**方法 2：推送 Tag**

```bash
git tag v1.0.0
git push origin v1.0.0
```

**方法 3：手动触发**

1. 在 GitHub 仓库，点击 "Actions"
2. 选择 "Build and Release" 工作流
3. 点击 "Run workflow"

#### 构建产物

工作流自动生成以下文件并附加到 Release：

- `MarketAssistant-Windows-x64.zip` - Windows 便携版
- `MarketAssistant-1.0.0.dmg` - macOS 磁盘映像
- `MarketAssistant_1.0.0_amd64.deb` - Debian/Ubuntu 安装包
- `MarketAssistant-1.0.0-x86_64.rpm` - Fedora/RHEL 安装包
- `MarketAssistant-Linux-x64.zip` - Linux 便携版

---

## 代码签名

### Windows 代码签名

```powershell
# 使用 signtool 签名
signtool sign /f "certificate.pfx" /p "password" /t http://timestamp.digicert.com "MarketAssistant.exe"
```

**GitHub Actions 配置：**

在仓库 Settings → Secrets 中添加：
- `WINDOWS_CERTIFICATE` - Base64 编码的 PFX 证书
- `WINDOWS_CERTIFICATE_PASSWORD` - 证书密码

### macOS 代码签名和公证

#### 本地配置

```bash
# 1. 设置签名标识（开发者 ID）
export SIGNING_IDENTITY="Developer ID Application: Your Name (TEAM_ID)"

# 2. 配置公证工具（需要 App Store Connect API）
xcrun notarytool store-credentials "AC_PASSWORD" \
  --apple-id "your.email@example.com" \
  --team-id TEAM_ID \
  --password "app-specific-password"

# 3. 设置环境变量
export NOTARYTOOL_PROFILE="AC_PASSWORD"

# 4. 运行构建（自动签名和公证）
./scripts/macos/build-app-bundle.sh
```

#### GitHub Actions 配置

在仓库 Settings → Secrets 中添加：
- `MACOS_CERTIFICATE` - Base64 编码的 .p12 证书
- `MACOS_CERTIFICATE_PWD` - 证书密码
- `KEYCHAIN_PASSWORD` - Keychain 密码（可自定义）
- `APPLE_ID` - Apple ID 邮箱
- `TEAM_ID` - 团队 ID
- `NOTARY_TOOL_PASSWORD` - App-specific password
- `MACOS_SIGNING_IDENTITY` - 证书指纹或名称

然后在 `.github/workflows/release.yml` 中取消注释代码签名相关部分。

#### 获取 App-Specific Password

1. 访问 [appleid.apple.com](https://appleid.apple.com)
2. 登录 Apple ID
3. 在"安全"部分，生成 App-specific password
4. 保存密码并添加到 GitHub Secrets

### Linux

Linux 不需要代码签名，但可以使用 GPG 签名 .deb 包：

```bash
dpkg-sig --sign builder MarketAssistant_1.0.0_amd64.deb
```

---

## Native AOT（可选）

可以启用 Native AOT 以减小包大小和提高性能。参考 [Avalonia Native AOT 部署指南](https://docs.avaloniaui.net/docs/deployment/native-aot)。

### 启用 Native AOT

在 `MarketAssistant.csproj` 中添加：

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
</PropertyGroup>
```

### 限制

- 某些反射功能可能不可用
- 需要额外配置 trim 警告
- 不支持所有 NuGet 包

---

## 发布配置说明

### 通用配置

- **目标框架**: `net9.0`
- **UI 框架**: Avalonia UI 11.3.7
- **发布模式**: 自包含 (self-contained)
- **ReadyToRun**: 启用（提高启动性能）
- **UseAppHost**: 启用（创建平台原生可执行文件）

### 平台运行时标识符

- **Windows**: `win-x64`
- **macOS Intel**: `osx-x64`
- **macOS Apple Silicon**: `osx-arm64`
- **Linux**: `linux-x64`

---

## 故障排除

### 常见问题

#### 1. 构建失败 - 缺少依赖项

```bash
dotnet restore MarketAssistant.slnx
dotnet clean MarketAssistant.slnx
dotnet restore src/MarketAssistant.csproj
```

#### 2. Playwright 浏览器未安装

```bash
dotnet tool update --global Microsoft.Playwright.CLI
playwright install
```

#### 3. macOS: "App is damaged and can't be opened"

这通常是因为 Gatekeeper 阻止了未签名的应用。解决方法：

```bash
# 移除隔离属性
xattr -cr /path/to/MarketAssistant.app

# 或者允许运行未签名应用
sudo spctl --master-disable
```

#### 4. Linux: .deb 安装失败 - 依赖问题

```bash
# 安装缺失的依赖
sudo apt-get install -f

# 或者手动安装依赖
sudo apt-get install libicu70 libssl3
```

#### 5. 跨平台构建注意事项

- Windows/Linux 版本可以在任何平台上构建
- macOS `.app` bundle 和 `.dmg` 只能在 macOS 上创建
- Linux `.deb` 和 `.rpm` 包最好在 Linux 上创建
- 发布时使用 `--self-contained` 以包含所有运行时依赖

### 日志和调试

#### 本地构建日志

```bash
# 详细构建输出
dotnet publish src/MarketAssistant.csproj -c Release -r win-x64 -v detailed
```

#### GitHub Actions 日志

- 在 GitHub 仓库的 "Actions" 页面查看
- 每个 job 都有独立的日志
- 可以下载 artifacts 进行本地测试

### 验证构建产物

#### Windows

```powershell
# 列出文件
Get-ChildItem -Recurse ./publish/windows

# 运行
./publish/windows/MarketAssistant.exe
```

#### macOS

```bash
# 验证 bundle 结构
ls -la Release/macOS/MarketAssistant.app/Contents/

# 验证签名
codesign --verify --deep --strict --verbose=2 Release/macOS/MarketAssistant.app

# 显示签名信息
codesign -dv --verbose=4 Release/macOS/MarketAssistant.app

# 测试运行
open Release/macOS/MarketAssistant.app
```

#### Linux

```bash
# 验证 .deb 包
dpkg-deb --info Release/Linux/MarketAssistant_1.0.0_amd64.deb
dpkg-deb --contents Release/Linux/MarketAssistant_1.0.0_amd64.deb

# 测试运行（从 publish 目录）
./publish/linux/MarketAssistant
```

---

## 更新版本号

在发布新版本前，更新以下文件：

### 1. 项目文件

```xml
<!-- src/MarketAssistant.csproj -->
<PropertyGroup>
  <Version>1.0.0</Version>
  <CFBundleVersion>1.0.0</CFBundleVersion>
  <CFBundleShortVersionString>1.0.0</CFBundleShortVersionString>
</PropertyGroup>
```

### 2. macOS Info.plist

```xml
<!-- scripts/macos/Info.plist.template -->
<key>CFBundleVersion</key>
<string>1.0.0</string>
<key>CFBundleShortVersionString</key>
<string>1.0.0</string>
```

### 3. 构建脚本

更新脚本中的 `VERSION` 变量：
- `scripts/macos/build-app-bundle.sh`
- `scripts/linux/build-deb.sh`
- `scripts/linux/build-rpm.sh`

---

## 相关文档

- [Avalonia UI 官方文档](https://docs.avaloniaui.net/)
- [Avalonia macOS 部署](https://docs.avaloniaui.net/docs/deployment/macOS)
- [Avalonia Debian/Ubuntu 打包](https://docs.avaloniaui.net/docs/deployment/debian-ubuntu)
- [Avalonia Native AOT](https://docs.avaloniaui.net/docs/deployment/native-aot)
- [.NET 9.0 发布指南](https://docs.microsoft.com/dotnet/core/deploying/)
- [AGENTS.md](../AGENTS.md) - 开发者指南

---

## 联系和支持

如遇到构建问题，请：

1. 查看本文档的"故障排除"部分
2. 检查 GitHub Actions 日志
3. 在 GitHub 仓库提交 Issue

---

**最后更新**: 2025-10-11  
**适用版本**: MarketAssistant 1.0.0+
