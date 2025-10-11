# GitHub Actions 工作流说明

本目录包含 MarketAssistant 的 CI/CD 自动化工作流。

## 📁 工作流文件

### build.yml - 持续集成
**触发条件：**
- Push to: `main`, `develop`, `feat/*`
- Pull Request to: `main`, `develop`

**任务：**
- ✅ 运行单元测试
- ✅ 在三个平台验证构建（Windows, macOS, Linux）
- ✅ 不创建安装包（快速反馈）

**用途：** 开发过程中及早发现问题

### release.yml - 发布构建
**触发条件：**
- 创建 Release
- 推送版本标签 (如 `v1.0.0`)
- 手动触发

**任务：**
- ✅ 构建所有平台的完整安装包
- ✅ 自动上传到 GitHub Release
- ✅ 支持代码签名（需配置）

**用途：** 正式发布版本

---

## 🧪 本地测试工作流（使用 act）

### 为什么需要本地测试？

- 🚀 快速验证工作流语法
- 💰 节省 GitHub Actions 使用时间
- 🔍 调试工作流问题
- ✅ 在推送前确保工作流正确

### 安装 act

```bash
# Windows - Chocolatey
choco install act-cli

# Windows - Scoop
scoop install act

# macOS
brew install act

# Linux
curl https://raw.githubusercontent.com/nektos/act/master/install.sh | sudo bash
```

### 快速开始

#### 1. 列出所有工作流

```powershell
.\scripts\test-workflows.ps1 -Workflow list
```

或直接使用 act:

```bash
act -l
```

输出示例：
```
Stage  Job             Workflow               Event
0      test            Build and Test         push
0      build-windows   Build and Test         push
0      build-macos     Build and Test         push
0      build-linux     Build and Test         push
0      build-windows   Build and Release      workflow_dispatch
0      build-macos     Build and Release      workflow_dispatch
0      build-linux     Build and Release      workflow_dispatch
```

#### 2. 测试 build.yml（推荐）

```powershell
# Dry run - 只查看将执行的步骤
.\scripts\test-workflows.ps1 -Workflow build -DryRun

# 实际运行
.\scripts\test-workflows.ps1 -Workflow build
```

或使用 act:

```bash
# Dry run
act push -W .github/workflows/build.yml -n

# 实际运行
act push -W .github/workflows/build.yml
```

#### 3. 测试单个 Job

```powershell
# 只测试 Windows 构建
.\scripts\test-workflows.ps1 -Workflow build -Job build-windows

# 只测试测试任务
.\scripts\test-workflows.ps1 -Workflow build -Job test
```

或使用 act:

```bash
act push -W .github/workflows/build.yml -j build-windows
```

#### 4. 测试 release.yml

```powershell
# ⚠️ 警告：release 工作流会执行完整构建，耗时较长
.\scripts\test-workflows.ps1 -Workflow release -DryRun
```

### act 常用命令

```bash
# 列出所有工作流和 jobs
act -l

# 测试 push 事件（build.yml）
act push

# 测试特定工作流文件
act push -W .github/workflows/build.yml

# 测试特定 job
act push -j test

# Dry run（不实际执行）
act -n

# 查看详细日志
act -v

# 使用特定平台
act -P ubuntu-latest=catthehacker/ubuntu:act-latest

# 传递 secrets
act --secret-file .github/workflows/.act/secrets
```

---

## 🔧 工作流配置详解

### build.yml 配置

```yaml
on:
  push:
    branches: [ main, develop, feat/* ]  # 监听这些分支的 push
  pull_request:
    branches: [ main, develop ]          # PR 到这些分支时触发
```

**Jobs:**
1. **test** - 运行单元测试（Ubuntu）
2. **build-windows** - 验证 Windows 构建
3. **build-macos** - 验证 macOS 构建
4. **build-linux** - 验证 Linux 构建

### release.yml 配置

```yaml
on:
  push:
    tags: ['v*']           # 推送 v* 标签时触发
  release:
    types: [created]       # 创建 Release 时触发
  workflow_dispatch:       # 手动触发
```

**Jobs:**
1. **build-windows** - 构建 Windows 安装包
2. **build-macos** - 构建 macOS .app 和 .dmg
3. **build-linux** - 构建 .deb 和 .rpm
4. **create-release** - 上传所有产物到 Release

---

## 📋 验证清单

### build.yml 验证

- [ ] 语法正确（`act -l` 能列出）
- [ ] 单元测试运行成功
- [ ] Windows 构建通过
- [ ] macOS 构建通过（带 -r osx-x64）
- [ ] Linux 构建通过（带 -r linux-x64）

### release.yml 验证

- [ ] 语法正确
- [ ] Windows 构建生成 ZIP
- [ ] macOS 构建生成 DMG
- [ ] Linux 构建生成 .deb
- [ ] Artifacts 正确上传
- [ ] Release 创建成功（需 GitHub）

### 本地 act 测试

```bash
# 1. 检查语法
act -l

# 2. Dry run build.yml
act push -W .github/workflows/build.yml -n

# 3. 测试单元测试 job
act push -W .github/workflows/build.yml -j test

# 4. 测试 Windows 构建
act push -W .github/workflows/build.yml -j build-windows
```

---

## 🐛 常见问题

### 问题 1: act 提示镜像太大

**解决方案：** 使用中等大小镜像（已在 `.actrc` 中配置）

```bash
# 或手动指定
act -P ubuntu-latest=catthehacker/ubuntu:act-latest
```

### 问题 2: .NET SDK 版本不匹配

**检查：**
- 工作流中的 `DOTNET_VERSION` 是否正确（当前 9.0.x）
- 本地 .NET SDK 版本

```bash
dotnet --version
```

### 问题 3: act 在 Windows 上需要 Docker

**要求：**
- Docker Desktop for Windows
- WSL2 后端

### 问题 4: 工作流在 GitHub 上失败但本地成功

**原因：**
- act 使用 Linux 容器模拟，无法完全模拟 Windows/macOS
- 某些步骤在 act 中可能跳过

**建议：**
- 对于关键更改，推送到测试分支验证
- 使用 `workflow_dispatch` 手动触发测试

---

## 🚀 触发工作流

### 触发 build.yml

```bash
# 推送到监听的分支
git push origin develop

# 创建 PR
gh pr create --base main
```

### 触发 release.yml

#### 方法 1: 创建 Release（推荐）

在 GitHub UI：
1. Releases → Create a new release
2. 创建标签（如 `v1.0.0`）
3. 填写发布说明
4. Publish release

#### 方法 2: 推送标签

```bash
git tag v1.0.0
git push origin v1.0.0
```

#### 方法 3: 手动触发

在 GitHub UI：
1. Actions → Build and Release
2. Run workflow

或使用 CLI:

```bash
gh workflow run release.yml
```

---

## 📊 监控工作流

### 在 GitHub

1. 访问仓库 → Actions
2. 选择工作流运行记录
3. 查看日志和 Artifacts

### 下载 Artifacts

```bash
# 使用 GitHub CLI
gh run list
gh run download <run-id>
```

---

## 🔐 配置代码签名

### macOS 代码签名

在仓库 Settings → Secrets and variables → Actions 添加：

| Secret | 说明 |
|--------|------|
| `MACOS_CERTIFICATE` | Base64 编码的 .p12 证书 |
| `MACOS_CERTIFICATE_PWD` | 证书密码 |
| `APPLE_ID` | Apple ID 邮箱 |
| `TEAM_ID` | 团队 ID |
| `NOTARY_TOOL_PASSWORD` | App-specific password |
| `MACOS_SIGNING_IDENTITY` | 证书指纹或名称 |
| `KEYCHAIN_PASSWORD` | Keychain 密码（自定义） |

**获取 Base64 编码的证书：**

```bash
# macOS/Linux
base64 -i certificate.p12 -o certificate.base64

# Windows
certutil -encode certificate.p12 certificate.base64
```

然后将 `certificate.base64` 的内容添加到 Secret。

### Windows 代码签名

添加这些 Secrets:
- `WINDOWS_CERTIFICATE`
- `WINDOWS_CERTIFICATE_PASSWORD`

---

## 📚 参考资料

- [GitHub Actions 文档](https://docs.github.com/en/actions)
- [act 文档](https://github.com/nektos/act)
- [actions/checkout](https://github.com/actions/checkout)
- [actions/setup-dotnet](https://github.com/actions/setup-dotnet)
- [actions/upload-artifact](https://github.com/actions/upload-artifact)
- [softprops/action-gh-release](https://github.com/softprops/action-gh-release)

---

## 💡 最佳实践

1. **本地先测试** - 使用 act 验证后再推送
2. **小步提交** - 分步验证工作流更改
3. **使用 Dry Run** - 先 dry run 再实际运行
4. **监控用量** - 注意 GitHub Actions 使用时间
5. **缓存依赖** - 使用 actions/cache 加速构建（可选）

---

**维护者**: MarketAssistant Team  
**最后更新**: 2025-10-11

