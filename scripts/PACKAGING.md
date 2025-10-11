# MarketAssistant 打包详细指南

本文档提供 MarketAssistant 各平台打包的详细技术说明，包括文件结构、配置细节和最佳实践。

## 目录

- [macOS 打包](#macos-打包)
- [Linux 打包](#linux-打包)
- [Windows 打包](#windows-打包)
- [版本管理](#版本管理)
- [最佳实践](#最佳实践)

---

## macOS 打包

### .app Bundle 结构

标准的 macOS 应用程序 bundle 结构：

```
MarketAssistant.app/
├── Contents/
│   ├── Info.plist                    # 应用元数据
│   ├── MacOS/                        # 可执行文件和库
│   │   ├── MarketAssistant          # 主可执行文件（无扩展名）
│   │   ├── MarketAssistant.dll      # .NET 应用程序集
│   │   ├── *.dll                    # 依赖库
│   │   └── config/                  # 配置文件
│   ├── Resources/                    # 资源文件
│   │   └── MarketAssistant.icns    # 应用图标
│   └── _CodeSignature/              # 代码签名信息（如果已签名）
│       └── CodeResources
```

### Info.plist 关键配置

```xml
<!-- CFBundleExecutable 必须匹配可执行文件名 -->
<key>CFBundleExecutable</key>
<string>MarketAssistant</string>

<!-- Bundle ID 使用反向 DNS 格式 -->
<key>CFBundleIdentifier</key>
<string>com.marketassistant.app</string>

<!-- 短名称（最多 15 字符） -->
<key>CFBundleName</key>
<string>MarketAssistant</string>

<!-- 完整显示名称 -->
<key>CFBundleDisplayName</key>
<string>Market Assistant</string>

<!-- 版本号（构建版本） -->
<key>CFBundleVersion</key>
<string>1.0.0</string>

<!-- 用户可见版本 -->
<key>CFBundleShortVersionString</key>
<string>1.0.0</string>

<!-- 应用类型 -->
<key>CFBundlePackageType</key>
<string>APPL</string>

<!-- 图标文件名（含扩展名） -->
<key>CFBundleIconFile</key>
<string>MarketAssistant.icns</string>

<!-- 支持 Retina 显示 -->
<key>NSHighResolutionCapable</key>
<true/>

<!-- 最低系统版本 -->
<key>LSMinimumSystemVersion</key>
<string>10.15</string>

<!-- 应用分类 -->
<key>LSApplicationCategoryType</key>
<string>public.app-category.finance</string>
```

### Entitlements（授权）

```xml
<!-- 允许 JIT 编译（.NET 运行时需要） -->
<key>com.apple.security.cs.allow-jit</key>
<true/>

<!-- 允许执行未签名的可执行内存 -->
<key>com.apple.security.cs.allow-unsigned-executable-memory</key>
<true/>

<!-- 禁用库验证（自包含应用需要） -->
<key>com.apple.security.cs.disable-library-validation</key>
<true/>

<!-- 网络访问 -->
<key>com.apple.security.network.client</key>
<true/>
<key>com.apple.security.network.server</key>
<true/>

<!-- 文件访问 -->
<key>com.apple.security.files.user-selected.read-write</key>
<true/>
```

### 创建 ICNS 图标

#### 方法 1: 使用 sips（macOS 内置）

```bash
#!/bin/bash
mkdir -p iconset.iconset

# 生成各种尺寸
sips -z 16 16     logo.png --out iconset.iconset/icon_16x16.png
sips -z 32 32     logo.png --out iconset.iconset/icon_16x16@2x.png
sips -z 32 32     logo.png --out iconset.iconset/icon_32x32.png
sips -z 64 64     logo.png --out iconset.iconset/icon_32x32@2x.png
sips -z 128 128   logo.png --out iconset.iconset/icon_128x128.png
sips -z 256 256   logo.png --out iconset.iconset/icon_128x128@2x.png
sips -z 256 256   logo.png --out iconset.iconset/icon_256x256.png
sips -z 512 512   logo.png --out iconset.iconset/icon_256x256@2x.png
sips -z 512 512   logo.png --out iconset.iconset/icon_512x512.png
sips -z 1024 1024 logo.png --out iconset.iconset/icon_512x512@2x.png

# 转换为 ICNS
iconutil -c icns iconset.iconset -o MarketAssistant.icns

# 清理
rm -rf iconset.iconset
```

#### 方法 2: 使用 ImageMagick（跨平台）

```bash
# 安装 ImageMagick
brew install imagemagick  # macOS
sudo apt install imagemagick  # Linux

# 转换
convert logo.png -resize 512x512 icon.iconset/icon_512x512.png
iconutil -c icns icon.iconset
```

### 代码签名详解

#### 签名流程

```bash
# 1. 列出可用的签名标识
security find-identity -v -p codesigning

# 2. 签名所有可执行文件和库
find "MarketAssistant.app/Contents/MacOS" -type f \( -name "*.dylib" -o -perm +111 \) | while read file; do
    codesign --force --timestamp --options=runtime \
        --entitlements MarketAssistant.entitlements \
        --sign "Developer ID Application: Your Name (TEAM_ID)" \
        "$file"
done

# 3. 签名整个 bundle
codesign --force --timestamp --options=runtime \
    --entitlements MarketAssistant.entitlements \
    --sign "Developer ID Application: Your Name (TEAM_ID)" \
    MarketAssistant.app

# 4. 验证签名
codesign --verify --deep --strict --verbose=2 MarketAssistant.app

# 5. 显示签名详情
codesign -dv --verbose=4 MarketAssistant.app
```

#### Hardened Runtime

使用 `--options=runtime` 启用 hardened runtime，这是公证所必需的。

### 公证（Notarization）

#### 前置要求

- Apple Developer Program 账户
- App-Specific Password
- Xcode 13+ 或 Command Line Tools

#### 配置公证工具

```bash
# 存储凭证
xcrun notarytool store-credentials "AC_PASSWORD" \
    --apple-id "your.email@example.com" \
    --team-id TEAM_ID \
    --password "app-specific-password"
```

#### 公证流程

```bash
# 1. 创建 DMG 或 ZIP
hdiutil create -volname "MarketAssistant" \
    -srcfolder MarketAssistant.app \
    -ov -format UDZO \
    MarketAssistant.dmg

# 2. 提交公证
xcrun notarytool submit MarketAssistant.dmg \
    --keychain-profile "AC_PASSWORD" \
    --wait

# 3. 查看结果（如果失败）
xcrun notarytool log <submission-id> \
    --keychain-profile "AC_PASSWORD"

# 4. 附加公证票据
xcrun stapler staple MarketAssistant.app
xcrun stapler staple MarketAssistant.dmg

# 5. 验证公证
spctl -a -t exec -vv MarketAssistant.app
```

### DMG 创建

#### 基础 DMG

```bash
hdiutil create -volname "MarketAssistant" \
    -srcfolder MarketAssistant.app \
    -ov -format UDZO \
    MarketAssistant.dmg
```

#### 高级 DMG（带背景和图标布局）

```bash
# 1. 创建临时 DMG
hdiutil create -size 200m -fs HFS+ -volname "MarketAssistant" temp.dmg

# 2. 挂载
hdiutil attach temp.dmg -mountpoint /Volumes/MarketAssistant

# 3. 复制应用
cp -R MarketAssistant.app /Volumes/MarketAssistant/

# 4. 创建 Applications 链接
ln -s /Applications /Volumes/MarketAssistant/Applications

# 5. 设置背景（可选）
cp background.png /Volumes/MarketAssistant/.background/

# 6. 卸载
hdiutil detach /Volumes/MarketAssistant

# 7. 压缩
hdiutil convert temp.dmg -format UDZO -o MarketAssistant.dmg

# 8. 清理
rm temp.dmg
```

---

## Linux 打包

### Debian (.deb) 包结构

```
marketassistant_1.0.0_amd64.deb
├── DEBIAN/
│   ├── control           # 包元数据
│   ├── postinst          # 安装后脚本
│   ├── prerm             # 卸载前脚本
│   └── md5sums          # 文件校验和（可选）
├── opt/
│   └── MarketAssistant/  # 应用程序文件
│       ├── MarketAssistant  # 可执行文件
│       ├── *.dll        # 依赖库
│       └── config/      # 配置文件
├── usr/
│   ├── bin/
│   │   └── marketassistant -> /opt/MarketAssistant/MarketAssistant
│   └── share/
│       ├── applications/
│       │   └── marketassistant.desktop
│       └── icons/
│           └── hicolor/
│               └── 256x256/
│                   └── apps/
│                       └── marketassistant.png
```

### control 文件

```
Package: marketassistant
Version: 1.0.0
Section: misc
Priority: optional
Architecture: amd64
Installed-Size: 150000
Maintainer: MarketAssistant Team <support@marketassistant.com>
Homepage: https://github.com/yourusername/MarketAssistant
Description: AI-powered market analysis assistant
 Market Assistant is a cross-platform desktop application that provides
 AI-powered market analysis and insights. It features real-time data
 analysis, technical indicators, and intelligent recommendations.
Depends: libicu70 | libicu72, libssl3 | libssl1.1
```

### Desktop Entry

```ini
[Desktop Entry]
Version=1.0
Type=Application
Name=Market Assistant
Comment=AI-powered market analysis assistant
Exec=/opt/MarketAssistant/MarketAssistant
Icon=marketassistant
Terminal=false
Categories=Office;Finance;
StartupWMClass=MarketAssistant
Keywords=market;analysis;finance;trading;stocks;
```

### postinst 脚本

```bash
#!/bin/bash
set -e

# 更新桌面数据库
if command -v update-desktop-database &> /dev/null; then
    update-desktop-database -q
fi

# 更新图标缓存
if command -v gtk-update-icon-cache &> /dev/null; then
    gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor || true
fi

# 更新 MIME 数据库（如果需要文件关联）
if command -v update-mime-database &> /dev/null; then
    update-mime-database /usr/share/mime || true
fi

exit 0
```

### 构建 .deb 包

```bash
# 创建包目录
mkdir -p marketassistant_1.0.0_amd64/DEBIAN
mkdir -p marketassistant_1.0.0_amd64/opt/MarketAssistant
mkdir -p marketassistant_1.0.0_amd64/usr/share/applications
mkdir -p marketassistant_1.0.0_amd64/usr/share/icons/hicolor/256x256/apps
mkdir -p marketassistant_1.0.0_amd64/usr/bin

# 复制文件
cp -r publish/* marketassistant_1.0.0_amd64/opt/MarketAssistant/
cp marketassistant.desktop marketassistant_1.0.0_amd64/usr/share/applications/
cp logo.png marketassistant_1.0.0_amd64/usr/share/icons/hicolor/256x256/apps/marketassistant.png

# 创建符号链接
cd marketassistant_1.0.0_amd64/usr/bin
ln -s /opt/MarketAssistant/MarketAssistant marketassistant
cd -

# 设置权限
chmod 755 marketassistant_1.0.0_amd64/DEBIAN/postinst
chmod 755 marketassistant_1.0.0_amd64/DEBIAN/prerm
chmod 755 marketassistant_1.0.0_amd64/opt/MarketAssistant/MarketAssistant

# 构建包
dpkg-deb --build --root-owner-group marketassistant_1.0.0_amd64

# 验证
dpkg-deb --info marketassistant_1.0.0_amd64.deb
dpkg-deb --contents marketassistant_1.0.0_amd64.deb
lintian marketassistant_1.0.0_amd64.deb
```

### RPM 包（Fedora/RHEL/CentOS）

#### Spec 文件结构

```spec
Name:           marketassistant
Version:        1.0.0
Release:        1%{?dist}
Summary:        AI-powered market analysis assistant
License:        Proprietary
URL:            https://github.com/yourusername/MarketAssistant
BuildArch:      x86_64

Requires:       libicu >= 60, openssl-libs >= 1.1

%description
Market Assistant is a cross-platform desktop application that provides
AI-powered market analysis and insights.

%install
rm -rf %{buildroot}
mkdir -p %{buildroot}/opt/MarketAssistant
mkdir -p %{buildroot}/usr/share/applications
mkdir -p %{buildroot}/usr/share/icons/hicolor/256x256/apps
mkdir -p %{buildroot}/usr/bin

cp -r %{_sourcedir}/publish/* %{buildroot}/opt/MarketAssistant/
cp %{_sourcedir}/marketassistant.desktop %{buildroot}/usr/share/applications/
cp %{_sourcedir}/logo.png %{buildroot}/usr/share/icons/hicolor/256x256/apps/marketassistant.png
ln -s /opt/MarketAssistant/MarketAssistant %{buildroot}/usr/bin/marketassistant

%files
/opt/MarketAssistant/*
/usr/share/applications/marketassistant.desktop
/usr/share/icons/hicolor/256x256/apps/marketassistant.png
/usr/bin/marketassistant

%post
update-desktop-database &> /dev/null || true
gtk-update-icon-cache -q /usr/share/icons/hicolor || true

%postun
update-desktop-database &> /dev/null || true

%changelog
* Sat Oct 11 2025 MarketAssistant Team <support@marketassistant.com> - 1.0.0-1
- Initial RPM release
```

#### 构建 RPM

```bash
# 创建构建目录
mkdir -p ~/rpmbuild/{BUILD,RPMS,SOURCES,SPECS,SRPMS}

# 复制文件
cp marketassistant.spec ~/rpmbuild/SPECS/
cp -r publish ~/rpmbuild/SOURCES/
cp marketassistant.desktop ~/rpmbuild/SOURCES/
cp logo.png ~/rpmbuild/SOURCES/

# 构建
rpmbuild -bb ~/rpmbuild/SPECS/marketassistant.spec

# 验证
rpm -qip ~/rpmbuild/RPMS/x86_64/marketassistant-1.0.0-1.x86_64.rpm
rpm -qlp ~/rpmbuild/RPMS/x86_64/marketassistant-1.0.0-1.x86_64.rpm
```

---

## Windows 打包

### 文件结构

```
MarketAssistant/
├── MarketAssistant.exe      # 主可执行文件
├── MarketAssistant.dll      # 应用程序集
├── *.dll                    # 依赖库
├── config/
│   └── models.yaml
└── Assets/
    └── ...
```

### 创建安装程序（可选）

使用 **Inno Setup** 或 **WiX Toolset**：

#### Inno Setup 脚本示例

```ini
[Setup]
AppName=Market Assistant
AppVersion=1.0.0
DefaultDirName={pf}\MarketAssistant
DefaultGroupName=Market Assistant
OutputDir=output
OutputBaseFilename=MarketAssistant-Setup-1.0.0
Compression=lzma2
SolidCompression=yes

[Files]
Source: "publish\windows\*"; DestDir: "{app}"; Flags: recursesubdirs

[Icons]
Name: "{group}\Market Assistant"; Filename: "{app}\MarketAssistant.exe"
Name: "{commondesktop}\Market Assistant"; Filename: "{app}\MarketAssistant.exe"

[Run]
Filename: "{app}\MarketAssistant.exe"; Description: "Launch Market Assistant"; Flags: nowait postinstall skipifsilent
```

---

## 版本管理

### 统一版本号

在一个地方定义版本号，然后在所有构建脚本中引用：

#### 方法 1: 使用 Directory.Build.props

```xml
<!-- Directory.Build.props -->
<Project>
  <PropertyGroup>
    <Version>1.0.0</Version>
    <CFBundleVersion>1.0.0</CFBundleVersion>
    <CFBundleShortVersionString>1.0.0</CFBundleShortVersionString>
  </PropertyGroup>
</Project>
```

#### 方法 2: 使用环境变量

```bash
# 在 CI/CD 中
export APP_VERSION="1.0.0"

# 在构建脚本中使用
dotnet publish -p:Version=$APP_VERSION
```

### 版本号格式

遵循 [语义化版本](https://semver.org/)：

- **Major.Minor.Patch** (1.0.0)
- **Major.Minor.Patch-Prerelease** (1.0.0-beta.1)
- **Major.Minor.Patch+Build** (1.0.0+20251011)

---

## 最佳实践

### 1. 文件权限

- **可执行文件**: `755` (rwxr-xr-x)
- **库文件**: `644` (rw-r--r--)
- **配置文件**: `644`
- **脚本**: `755`

### 2. 文件大小优化

```bash
# 移除调试符号
strip MarketAssistant

# 移除不必要的文件
find . -name "*.pdb" -delete
find . -name "*.xml" -delete  # API 文档

# 使用 Single File 发布（可选）
dotnet publish -p:PublishSingleFile=true
```

### 3. 依赖管理

- **自包含发布**: 包含 .NET 运行时（体积大，无需预装）
- **框架依赖发布**: 需要预装 .NET（体积小）

```bash
# 自包含（推荐）
dotnet publish --self-contained

# 框架依赖
dotnet publish --self-contained false
```

### 4. 测试打包结果

#### macOS
```bash
# 测试运行
open MarketAssistant.app

# 测试安装
open MarketAssistant.dmg

# 验证签名
codesign --verify --deep --strict --verbose=2 MarketAssistant.app
spctl -a -t exec -vv MarketAssistant.app
```

#### Linux
```bash
# 测试 .deb 安装
sudo dpkg -i marketassistant_1.0.0_amd64.deb
marketassistant

# 测试卸载
sudo apt remove marketassistant
```

#### Windows
```bash
# 测试运行
.\MarketAssistant.exe

# 测试便携版
cd publish\windows
.\MarketAssistant.exe
```

### 5. 错误处理

在构建脚本中添加错误检查：

```bash
#!/bin/bash
set -e  # 遇到错误立即退出
set -u  # 使用未定义变量时退出
set -o pipefail  # 管道命令失败时退出
```

### 6. 清理和维护

```bash
# 清理旧构建
rm -rf Release/
rm -rf publish/

# 清理 NuGet 缓存
dotnet nuget locals all --clear

# 清理构建缓存
dotnet clean
```

---

## 参考资料

- [Avalonia macOS 部署](https://docs.avaloniaui.net/docs/deployment/macOS)
- [Avalonia Debian/Ubuntu 打包](https://docs.avaloniaui.net/docs/deployment/debian-ubuntu)
- [Apple 代码签名指南](https://developer.apple.com/documentation/security/notarizing_macos_software_before_distribution)
- [Debian 打包指南](https://www.debian.org/doc/manuals/maint-guide/)
- [RPM 打包指南](https://rpm-packaging-guide.github.io/)

---

**维护者**: MarketAssistant Team  
**最后更新**: 2025-10-11

