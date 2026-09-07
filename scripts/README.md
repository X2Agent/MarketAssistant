# MarketAssistant 构建脚本

本目录包含 MarketAssistant 跨平台构建和打包脚本。

## 📁 目录结构

```
scripts/
├── build-release.ps1          # 主构建脚本（跨平台）
├── BUILD.md                   # 详细构建文档
├── README.md                  # 本文件
├── macos/                     # macOS 打包资源
│   ├── build-app-bundle.sh   # macOS .app bundle 构建脚本
│   ├── Info.plist.template   # 应用元数据模板
│   ├── MarketAssistant.entitlements  # 应用授权
│   └── CREATE_ICON.md        # 图标创建指南
└── linux/                     # Linux 打包资源
    ├── build-deb.sh          # Debian/Ubuntu .deb 构建
    ├── build-rpm.sh          # Fedora/RHEL .rpm 构建
    └── marketassistant.desktop  # Desktop 入口文件
```

## 🚀 快速开始

### 构建当前平台

```powershell
# Windows
.\scripts\build-release.ps1

# macOS/Linux
pwsh scripts/build-release.ps1 -Platform macOS
```

### 构建所有平台

```powershell
.\scripts\build-release.ps1 -Platform All
```

## 📦 构建产物

### Windows
- `MarketAssistant-Windows-x64.zip` - 便携版

### macOS
- `MarketAssistant.app` - 应用程序 bundle
- `MarketAssistant-1.0.0.dmg` - 磁盘映像（只在 macOS 上生成）

### Linux
- `MarketAssistant_1.0.0_amd64.deb` - Debian/Ubuntu 安装包
- `MarketAssistant-1.0.0-x86_64.rpm` - Fedora/RHEL 安装包
- `MarketAssistant-Linux-x64.zip` - 便携版

## 📖 详细文档

- **[BUILD.md](BUILD.md)** - 完整的构建和发布指南
  - 平台特定构建说明
  - 代码签名配置
  - GitHub Actions 自动化
  - 故障排除

- **[macos/CREATE_ICON.md](macos/CREATE_ICON.md)** - macOS 图标创建
  - 从 PNG 创建 ICNS
  - 使用 sips 和 iconutil
  - 图标设计建议

## 🔧 脚本说明

### build-release.ps1

主构建脚本，支持所有平台。

**参数：**
- `-Platform` - 目标平台 (Windows, macOS, Linux, All)
- `-Configuration` - 构建配置 (Debug, Release)

**示例：**
```powershell
# 构建 Windows Release
.\scripts\build-release.ps1 -Platform Windows -Configuration Release

# 构建所有平台
.\scripts\build-release.ps1 -Platform All
```

### macOS 脚本

#### build-app-bundle.sh

创建标准的 macOS .app bundle 和 DMG。

**环境变量：**
- `SIGNING_IDENTITY` - 代码签名标识（可选）
- `NOTARYTOOL_PROFILE` - 公证工具配置（可选）

**示例：**
```bash
# 基础构建（无签名）
./scripts/macos/build-app-bundle.sh

# 带签名的构建
export SIGNING_IDENTITY="Developer ID Application: Your Name (TEAM_ID)"
./scripts/macos/build-app-bundle.sh

# 带签名和公证
export SIGNING_IDENTITY="Developer ID Application: Your Name (TEAM_ID)"
export NOTARYTOOL_PROFILE="AC_PASSWORD"
./scripts/macos/build-app-bundle.sh
```

### Linux 脚本

#### build-deb.sh

创建 Debian/Ubuntu .deb 安装包。

```bash
./scripts/linux/build-deb.sh
```

#### build-rpm.sh

创建 Fedora/RHEL .rpm 安装包。

```bash
./scripts/linux/build-rpm.sh
```

## 🔐 代码签名

### Windows

需要 Code Signing 证书 (.pfx)。

### macOS

需要：
- Apple Developer Program 会员
- Developer ID Application 证书
- App-Specific Password（用于公证）

**配置签名：**
```bash
# 1. 导入证书到 Keychain
security import certificate.p12 -k ~/Library/Keychains/login.keychain

# 2. 配置公证工具
xcrun notarytool store-credentials "AC_PASSWORD" \
  --apple-id "your.email@example.com" \
  --team-id TEAM_ID \
  --password "app-specific-password"

# 3. 设置环境变量
export SIGNING_IDENTITY="Developer ID Application: Your Name (TEAM_ID)"
export NOTARYTOOL_PROFILE="AC_PASSWORD"

# 4. 构建（自动签名和公证）
./scripts/macos/build-app-bundle.sh
```

详见 [BUILD.md - 代码签名](BUILD.md#代码签名)

## 🤖 GitHub Actions

项目配置了两个 CI/CD 工作流：

### build.yml
- 在每次 push 和 PR 时运行
- 验证代码可以在所有平台构建
- 运行单元测试

### release.yml
- 在创建 Release 或推送 Tag 时运行
- 构建所有平台的安装包
- 自动上传到 GitHub Release

**触发发布：**
```bash
git tag v1.0.0
git push origin v1.0.0
```

详见 [BUILD.md - 自动化发布](BUILD.md#自动化发布-github-actions)

## 📋 前置要求

### 所有平台
- .NET 10.0 SDK
- PowerShell Core (用于运行主脚本)

### macOS
- Xcode Command Line Tools
- (可选) Apple Developer 账户（用于签名和公证）

### Linux
- dpkg-deb（Debian/Ubuntu 默认包含）
- (可选) rpmbuild（用于创建 RPM 包）

## 🐛 故障排除

### 问题：脚本没有执行权限

```bash
# macOS/Linux
chmod +x scripts/macos/build-app-bundle.sh
chmod +x scripts/linux/build-deb.sh
chmod +x scripts/linux/build-rpm.sh
```

### 问题：PowerShell 执行策略

```powershell
# Windows
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

### 问题：构建失败 - 依赖项

```bash
dotnet restore MarketAssistant.slnx
dotnet clean MarketAssistant.slnx
```

更多故障排除，参见 [BUILD.md - 故障排除](BUILD.md#故障排除)

## 📚 相关文档

- [BUILD.md](BUILD.md) - 详细构建指南
- [../AGENTS.md](../AGENTS.md) - 开发者指南
- [../README.md](../README.md) - 项目主文档

## 🔗 外部资源

- [Avalonia macOS 部署](https://docs.avaloniaui.net/docs/deployment/macOS)
- [Avalonia Debian/Ubuntu 打包](https://docs.avaloniaui.net/docs/deployment/debian-ubuntu)
- [Avalonia Native AOT](https://docs.avaloniaui.net/docs/deployment/native-aot)
- [Apple 代码签名指南](https://developer.apple.com/documentation/security/notarizing_macos_software_before_distribution)
- [Debian 打包指南](https://www.debian.org/doc/manuals/maint-guide/)

---

**维护者**: MarketAssistant Team  
**最后更新**: 2025-10-11

