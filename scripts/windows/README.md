# Windows 安装程序构建

本目录包含 Windows 平台的安装程序构建脚本和配置。

## 📦 文件说明

- `MarketAssistant.iss` - Inno Setup 配置脚本
- `build-installer.ps1` - 自动化构建脚本
- `README.md` - 本文件

## 🚀 快速开始

### 1. 安装 Inno Setup

下载并安装 Inno Setup 6.0+：

**方法 1：官方下载**
- 访问：https://jrsoftware.org/isdl.php
- 下载并安装

**方法 2：使用 Chocolatey**
```powershell
choco install innosetup
```

**方法 3：使用 Winget**
```powershell
winget install --id JRSoftware.InnoSetup
```

### 2. 构建应用

```powershell
# 首先构建 Windows 版本
.\scripts\build-release.ps1 -Platform Windows
```

### 3. 创建安装程序

```powershell
# 使用构建脚本（推荐）
.\scripts\windows\build-installer.ps1

# 或指定版本号
.\scripts\windows\build-installer.ps1 -Version "1.0.0"

# 或手动编译
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" scripts\windows\MarketAssistant.iss
```

## 📦 产物

构建完成后，安装程序将生成在：

```
Release/Windows/
└── MarketAssistant-Setup-1.0.0.exe    (约 50-60 MB)
```

## ✨ 安装程序功能

### 安装向导

- ✅ 现代化的 Windows 11 风格 UI
- ✅ 支持中文和英文
- ✅ 自定义安装路径
- ✅ 创建桌面快捷方式（可选）
- ✅ 创建开始菜单项
- ✅ 显示许可协议
- ✅ 显示 README

### 安装功能

- ✅ 检测并关闭正在运行的实例
- ✅ 覆盖安装旧版本
- ✅ 注册卸载程序
- ✅ 安装后可选启动应用

### 卸载功能

- ✅ 完整卸载应用文件
- ✅ 可选删除用户数据
- ✅ 清理注册表项
- ✅ 移除快捷方式

## 🔧 自定义配置

编辑 `MarketAssistant.iss` 文件：

### 修改应用信息

```pascal
#define MyAppName "Market Assistant"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "MarketAssistant Team"
#define MyAppURL "https://github.com/yourusername/MarketAssistant"
```

### 修改安装选项

```pascal
[Setup]
DefaultDirName={autopf}\{#MyAppName}    ; 默认安装路径
DisableProgramGroupPage=yes             ; 禁用程序组选择
PrivilegesRequired=admin                ; 需要管理员权限
```

### 添加更多语言

```pascal
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "chinesetraditional"; MessagesFile: "compiler:Languages\ChineseTraditional.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
```

### 自定义安装任务

```pascal
[Tasks]
Name: "desktopicon"; Description: "创建桌面图标"
Name: "quicklaunchicon"; Description: "创建快速启动图标"
Name: "startmenu"; Description: "添加到开始菜单"
```

## 🧪 测试安装程序

### 本地测试

```powershell
# 运行安装程序
.\Release\Windows\MarketAssistant-Setup-1.0.0.exe

# 静默安装（测试用）
.\Release\Windows\MarketAssistant-Setup-1.0.0.exe /SILENT

# 非常静默安装（无 UI）
.\Release\Windows\MarketAssistant-Setup-1.0.0.exe /VERYSILENT
```

### 测试卸载

```powershell
# 从控制面板卸载
# 或运行卸载程序
"%ProgramFiles%\Market Assistant\unins000.exe"
```

### 验证清单

- [ ] 安装向导显示正常
- [ ] 可以选择安装路径
- [ ] 创建桌面快捷方式
- [ ] 应用可以正常启动
- [ ] 在"控制面板 → 程序"中显示
- [ ] 卸载完整且干净
- [ ] 图标显示正确

## 📋 命令行参数

Inno Setup 安装程序支持以下参数：

| 参数 | 说明 |
|-----|------|
| `/SILENT` | 静默安装，显示进度 |
| `/VERYSILENT` | 非常静默，不显示任何界面 |
| `/SUPPRESSMSGBOXES` | 抑制消息框 |
| `/NOCANCEL` | 禁用取消按钮 |
| `/NORESTART` | 安装后不重启 |
| `/DIR="x:\dirname"` | 指定安装目录 |
| `/GROUP="folder name"` | 指定开始菜单文件夹 |
| `/NOICONS` | 不创建图标 |
| `/TASKS="task1,task2"` | 指定任务 |

**示例：**

```powershell
# 静默安装到指定目录
MarketAssistant-Setup-1.0.0.exe /VERYSILENT /DIR="D:\Apps\MarketAssistant"

# 静默安装且不创建桌面图标
MarketAssistant-Setup-1.0.0.exe /SILENT /TASKS="!desktopicon"
```

## 🤖 GitHub Actions 集成

更新 `.github/workflows/release.yml`：

```yaml
build-windows:
  runs-on: windows-latest
  steps:
    - name: Checkout code
      uses: actions/checkout@v4
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: 9.0.x
    
    - name: Install Inno Setup
      run: choco install innosetup -y
    
    - name: Restore dependencies
      run: dotnet restore MarketAssistant.slnx
    
    - name: Publish Windows
      run: |
        dotnet publish src/MarketAssistant.csproj `
          -c Release -r win-x64 --self-contained `
          -o ./Release/Windows-x64
    
    - name: Build installer
      run: .\scripts\windows\build-installer.ps1
    
    - name: Upload installer
      uses: actions/upload-artifact@v4
      with:
        name: windows-installer
        path: Release/Windows/MarketAssistant-Setup-*.exe
```

## 🔐 代码签名（可选）

### 签名安装程序

如果有代码签名证书，可以签名安装程序：

```powershell
# 使用 signtool
signtool sign /f "certificate.pfx" /p "password" /t http://timestamp.digicert.com "MarketAssistant-Setup-1.0.0.exe"
```

### 在 Inno Setup 中配置签名

编辑 `MarketAssistant.iss`：

```pascal
[Setup]
; 签名工具配置
SignTool=signtool
SignedUninstaller=yes

; 在命令行调用时指定
; ISCC.exe /Ssigntool="signtool sign /f certificate.pfx /p password /t http://timestamp.digicert.com $f" MarketAssistant.iss
```

## 📊 文件大小对比

| 版本 | 大小 | 说明 |
|-----|------|------|
| ZIP 便携版 | ~45 MB | 需要解压 |
| .exe 安装程序 | ~50 MB | 包含安装逻辑 |
| Native AOT | ~25 MB | 启用 AOT 后 |

## 🐛 常见问题

### 问题 1: Inno Setup 找不到

**解决方案：**
- 确保安装路径为默认路径
- 或修改 `build-installer.ps1` 中的路径

### 问题 2: 编译失败 - 找不到源文件

**解决方案：**
```powershell
# 确保先构建应用
.\scripts\build-release.ps1 -Platform Windows
```

### 问题 3: 安装时提示"未知发布者"

**解决方案：**
- 对安装程序进行代码签名
- 或用户右键 → 属性 → 解除阻止

### 问题 4: 卸载后残留文件

**说明：** 用户数据默认保留，可在卸载时选择删除

## 📚 参考资料

- [Inno Setup 官方文档](https://jrsoftware.org/ishelp/)
- [Inno Setup 脚本参考](https://jrsoftware.org/ishelp/index.php?topic=scriptintro)
- [Inno Setup 示例](https://jrsoftware.org/ishelp/index.php?topic=examples)

---

**维护者**: MarketAssistant Team  
**最后更新**: 2025-10-11

