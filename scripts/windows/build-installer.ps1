# MarketAssistant Windows 安装程序构建脚本
# 使用 Inno Setup 创建 .exe 安装程序

param(
    [string]$Configuration = "Release",
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"

Write-Host "🚀 Building Windows Installer..." -ForegroundColor Cyan
Write-Host ""

# 检查 Inno Setup 是否安装
$innoSetupPath = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $innoSetupPath)) {
    Write-Host "❌ Inno Setup not found!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please install Inno Setup 6.0+:" -ForegroundColor Yellow
    Write-Host "  https://jrsoftware.org/isdl.php" -ForegroundColor White
    Write-Host ""
    Write-Host "Or install via Chocolatey:" -ForegroundColor Yellow
    Write-Host "  choco install innosetup" -ForegroundColor White
    Write-Host ""
    exit 1
}

Write-Host "✓ Inno Setup found: $innoSetupPath" -ForegroundColor Green
Write-Host ""

# 检查源文件是否存在
$publishDir = "Release\Windows-x64"
if (-not (Test-Path $publishDir)) {
    Write-Host "❌ Published files not found: $publishDir" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please run publish first:" -ForegroundColor Yellow
    Write-Host "  .\scripts\build-release.ps1 -Platform Windows" -ForegroundColor White
    Write-Host ""
    exit 1
}

Write-Host "✓ Source files found: $publishDir" -ForegroundColor Green
Write-Host ""

# 检查主可执行文件
$exePath = "$publishDir\MarketAssistant.exe"
if (-not (Test-Path $exePath)) {
    Write-Host "❌ MarketAssistant.exe not found in $publishDir" -ForegroundColor Red
    exit 1
}

Write-Host "✓ MarketAssistant.exe found" -ForegroundColor Green
Write-Host ""

# 更新版本号（如果提供）
$issFile = "scripts\windows\MarketAssistant.iss"
if ($Version) {
    Write-Host "📝 Updating version to $Version..." -ForegroundColor Yellow
    $issContent = Get-Content $issFile -Raw
    $issContent = $issContent -replace '#define MyAppVersion ".*?"', "#define MyAppVersion `"$Version`""
    Set-Content $issFile -Value $issContent -NoNewline
    Write-Host "✓ Version updated" -ForegroundColor Green
    Write-Host ""
}

# 构建安装程序
Write-Host "🔨 Building installer with Inno Setup..." -ForegroundColor Cyan
Write-Host ""

$buildStart = Get-Date

& $innoSetupPath $issFile

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "❌ Installer build failed!" -ForegroundColor Red
    exit 1
}

$buildTime = ((Get-Date) - $buildStart).TotalSeconds

Write-Host ""
Write-Host "✅ Installer built successfully!" -ForegroundColor Green
Write-Host ""

# 查找生成的安装程序
$installerPath = Get-ChildItem -Path "Release\Windows" -Filter "MarketAssistant-Setup-*.exe" -ErrorAction SilentlyContinue | 
                 Sort-Object LastWriteTime -Descending | 
                 Select-Object -First 1

if ($installerPath) {
    $installerSize = [math]::Round($installerPath.Length / 1MB, 2)
    Write-Host "📦 Installer:" -ForegroundColor Yellow
    Write-Host "   • File: $($installerPath.Name)" -ForegroundColor White
    Write-Host "   • Size: $installerSize MB" -ForegroundColor White
    Write-Host "   • Path: $($installerPath.FullName)" -ForegroundColor White
    Write-Host "   • Build time: $([math]::Round($buildTime, 1))s" -ForegroundColor White
} else {
    Write-Host "⚠️  Installer file not found in Release\Windows" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "🧪 To test the installer:" -ForegroundColor Cyan
Write-Host "   Run: $($installerPath.FullName)" -ForegroundColor White
Write-Host ""

