# MarketAssistant Build Script - Avalonia
# 跨平台构建脚本：Windows, macOS, Linux
# 遵循 Avalonia 官方部署规范

param(
    [string]$Platform = "Windows",  # Windows, macOS, Linux, All
    [string]$Configuration = "Release",
    [switch]$CreateInstaller = $false
)

$startTime = Get-Date

Write-Host "🚀 Building MarketAssistant - Platform: $Platform" -ForegroundColor Green

# 检查 .NET SDK
try {
    $version = dotnet --version
    Write-Host "✓ .NET $version" -ForegroundColor Green
} catch {
    Write-Host "✗ .NET SDK not found" -ForegroundColor Red
    exit 1
}

# 还原依赖
Write-Host "📦 Restoring dependencies..." -ForegroundColor Yellow
dotnet restore MarketAssistant.slnx --verbosity quiet
if ($LASTEXITCODE -ne 0) { exit 1 }

# 准备输出目录
$outputDir = "./Release"
if (Test-Path $outputDir) { Remove-Item $outputDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

# 构建统计
$buildStats = @()

# Windows 构建函数
function Build-Windows {
    Write-Host "`n🔨 Building Windows x64..." -ForegroundColor Cyan
    
    $publishDir = "$outputDir/Windows-x64"
    $buildStart = Get-Date
    
    dotnet publish src/MarketAssistant.App/MarketAssistant.App.csproj `
        -c $Configuration `
        -r win-x64 `
        --self-contained `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        --verbosity quiet `
        -o $publishDir
        
    if ($LASTEXITCODE -ne 0) {
        Write-Host "✗ Build failed" -ForegroundColor Red
        return $null
    }
    
    $buildTime = ((Get-Date) - $buildStart).TotalSeconds
    
    # 统计构建前大小
    $beforeSize = (Get-ChildItem -Path $publishDir -Recurse | Measure-Object -Property Length -Sum).Sum
    
    # 清理调试文件
    $debugFiles = Get-ChildItem -Path $publishDir -Include "*.pdb", "*.xml" -Recurse
    $cleanedSize = 0
    foreach ($file in $debugFiles) {
        $cleanedSize += $file.Length
        Remove-Item $file.FullName -Force
    }
    
    $afterSize = $beforeSize - $cleanedSize
    
    # 打包
    $zipPath = "$outputDir/MarketAssistant-Windows-x64.zip"
    Compress-Archive -Path "$publishDir/*" -DestinationPath $zipPath -CompressionLevel Optimal -Force
    
    $zipSize = (Get-Item $zipPath).Length
    
    Write-Host "   Build size: $([math]::Round($beforeSize/1MB, 2)) MB" -ForegroundColor Gray
    Write-Host "   Cleaned: $([math]::Round($cleanedSize/1MB, 2)) MB (debug files)" -ForegroundColor Gray
    Write-Host "   Final size: $([math]::Round($afterSize/1MB, 2)) MB" -ForegroundColor Gray
    Write-Host "   ZIP size: $([math]::Round($zipSize/1MB, 2)) MB" -ForegroundColor Yellow
    Write-Host "   Build time: $([math]::Round($buildTime, 1))s" -ForegroundColor Gray
    Write-Host "✓ Windows build completed" -ForegroundColor Green
    
    return @{
        Name = "Windows-x64"
        ZipPath = $zipPath
        ZipSize = $zipSize
        BuildSize = $afterSize
        BuildTime = $buildTime
    }
}

# macOS 构建函数
function Build-macOS {
    Write-Host "`n🔨 Building macOS..." -ForegroundColor Cyan
    
    $buildStart = Get-Date
    
    # 使用专用的 macOS 构建脚本
    if ($IsMacOS -or $IsLinux) {
        # 在 macOS/Linux 上执行 bash 脚本
        $scriptPath = "./scripts/macos/build-app-bundle.sh"
        
        if (-not (Test-Path $scriptPath)) {
            Write-Host "✗ macOS build script not found: $scriptPath" -ForegroundColor Red
            return $null
        }
        
        # 确保脚本有执行权限
        chmod +x $scriptPath 2>$null
        
        # 执行构建脚本
        & bash $scriptPath
        
        if ($LASTEXITCODE -ne 0) {
            Write-Host "✗ macOS build failed" -ForegroundColor Red
            return $null
        }
    } else {
        # 在 Windows 上进行基础构建（无 .app bundle）
        Write-Host "   ⚠ Building on Windows - .app bundle will not be created" -ForegroundColor Yellow
        
        $publishDir = "$outputDir/macOS-x64"
        
        dotnet publish src/MarketAssistant.App/MarketAssistant.App.csproj `
            -c $Configuration `
            -r osx-x64 `
            --self-contained `
            -p:DebugType=None `
            -p:DebugSymbols=false `
            --verbosity quiet `
            -o $publishDir
            
        if ($LASTEXITCODE -ne 0) {
            Write-Host "✗ Build failed" -ForegroundColor Red
            return $null
        }
        
        # 清理并打包
        Get-ChildItem -Path $publishDir -Include "*.pdb", "*.xml" -Recurse | Remove-Item -Force
        
        $zipPath = "$outputDir/MarketAssistant-macOS-x64.zip"
        Compress-Archive -Path "$publishDir/*" -DestinationPath $zipPath -CompressionLevel Optimal -Force
        
        $zipSize = (Get-Item $zipPath).Length
        Write-Host "   ZIP size: $([math]::Round($zipSize/1MB, 2)) MB" -ForegroundColor Yellow
    }
    
    $buildTime = ((Get-Date) - $buildStart).TotalSeconds
    
    Write-Host "✓ macOS build completed" -ForegroundColor Green
    
    # 查找生成的文件
    $dmgPath = Get-ChildItem -Path "$outputDir/macOS" -Filter "*.dmg" -ErrorAction SilentlyContinue | Select-Object -First 1
    $zipPath = Get-ChildItem -Path $outputDir -Filter "MarketAssistant-macOS*.zip" -ErrorAction SilentlyContinue | Select-Object -First 1
    
    $size = 0
    if ($dmgPath) {
        $size = $dmgPath.Length
    } elseif ($zipPath) {
        $size = $zipPath.Length
    }
    
    return @{
        Name = "macOS"
        ZipSize = $size
        BuildTime = $buildTime
    }
}

# Linux 构建函数
function Build-Linux {
    Write-Host "`n🔨 Building Linux..." -ForegroundColor Cyan
    
    $buildStart = Get-Date
    
    # 使用专用的 Linux 构建脚本
    if ($IsLinux) {
        $scriptPath = "./scripts/linux/build-deb.sh"
        
        if (-not (Test-Path $scriptPath)) {
            Write-Host "✗ Linux build script not found: $scriptPath" -ForegroundColor Red
            return $null
        }
        
        # 确保脚本有执行权限
        chmod +x $scriptPath 2>$null
        
        # 执行构建脚本
        & bash $scriptPath
        
        if ($LASTEXITCODE -ne 0) {
            Write-Host "✗ Linux build failed" -ForegroundColor Red
            return $null
        }
    } else {
        # 在非 Linux 上进行基础构建
        Write-Host "   ⚠ Building on non-Linux - .deb package will not be created" -ForegroundColor Yellow
        
        $publishDir = "$outputDir/Linux-x64"
        
        dotnet publish src/MarketAssistant.App/MarketAssistant.App.csproj `
            -c $Configuration `
            -r linux-x64 `
            --self-contained `
            -p:DebugType=None `
            -p:DebugSymbols=false `
            --verbosity quiet `
            -o $publishDir
            
        if ($LASTEXITCODE -ne 0) {
            Write-Host "✗ Build failed" -ForegroundColor Red
            return $null
        }
        
        # 清理并打包
        Get-ChildItem -Path $publishDir -Include "*.pdb", "*.xml" -Recurse | Remove-Item -Force
        
        $zipPath = "$outputDir/MarketAssistant-Linux-x64.zip"
        Compress-Archive -Path "$publishDir/*" -DestinationPath $zipPath -CompressionLevel Optimal -Force
        
        $zipSize = (Get-Item $zipPath).Length
        Write-Host "   ZIP size: $([math]::Round($zipSize/1MB, 2)) MB" -ForegroundColor Yellow
    }
    
    $buildTime = ((Get-Date) - $buildStart).TotalSeconds
    
    Write-Host "✓ Linux build completed" -ForegroundColor Green
    
    # 查找生成的文件
    $debPath = Get-ChildItem -Path "$outputDir/Linux" -Filter "*.deb" -ErrorAction SilentlyContinue | Select-Object -First 1
    $zipPath = Get-ChildItem -Path $outputDir -Filter "MarketAssistant-Linux*.zip" -ErrorAction SilentlyContinue | Select-Object -First 1
    
    $size = 0
    if ($debPath) {
        $size = $debPath.Length
    } elseif ($zipPath) {
        $size = $zipPath.Length
    }
    
    return @{
        Name = "Linux"
        ZipSize = $size
        BuildTime = $buildTime
    }
}

# 执行构建
$results = @()

if ($Platform -eq "All" -or $Platform -eq "Windows") {
    $result = Build-Windows
    if ($result) { $results += $result }
}

if ($Platform -eq "All" -or $Platform -eq "macOS") {
    $result = Build-macOS
    if ($result) { $results += $result }
}

if ($Platform -eq "All" -or $Platform -eq "Linux") {
    $result = Build-Linux
    if ($result) { $results += $result }
}

$totalTime = ((Get-Date) - $startTime).TotalSeconds

# 构建汇总
Write-Host "`n" + "="*60 -ForegroundColor Cyan
Write-Host "📊 Build Summary" -ForegroundColor Cyan
Write-Host "="*60 -ForegroundColor Cyan

if ($results.Count -gt 0) {
    Write-Host "`n📦 Generated Packages:" -ForegroundColor Yellow
    $totalSize = 0
    
    # 列出所有生成的文件
    $outputFiles = Get-ChildItem -Path $outputDir -Include "*.zip", "*.dmg", "*.deb", "*.rpm" -Recurse -ErrorAction SilentlyContinue
    
    foreach ($file in $outputFiles) {
        $size = [math]::Round($file.Length/1MB, 2)
        Write-Host "   • $($file.Name) - $size MB" -ForegroundColor White
        $totalSize += $file.Length
    }
    
    Write-Host "`n📈 Statistics:" -ForegroundColor Yellow
    Write-Host "   Platforms built: $($results.Count)" -ForegroundColor White
    if ($totalSize -gt 0) {
        Write-Host "   Total package size: $([math]::Round($totalSize/1MB, 2)) MB" -ForegroundColor White
    }
    Write-Host "   Total build time: $([math]::Round($totalTime, 1))s" -ForegroundColor White
    Write-Host "   Output directory: $outputDir" -ForegroundColor White
    
    Write-Host "`n✅ Build completed successfully!" -ForegroundColor Green
} else {
    Write-Host "`n❌ No packages were built" -ForegroundColor Red
}

Write-Host "`n" + "="*60 -ForegroundColor Cyan
Write-Host "📖 Usage Examples" -ForegroundColor Cyan
Write-Host "="*60 -ForegroundColor Cyan
Write-Host "   .\scripts\build-release.ps1                        # Build Windows (default)" -ForegroundColor White
Write-Host "   .\scripts\build-release.ps1 -Platform macOS        # Build macOS" -ForegroundColor White
Write-Host "   .\scripts\build-release.ps1 -Platform Linux        # Build Linux" -ForegroundColor White
Write-Host "   .\scripts\build-release.ps1 -Platform All          # Build all platforms" -ForegroundColor White
Write-Host ""
Write-Host "💡 Platform-specific packaging:" -ForegroundColor Cyan
Write-Host "   macOS: Run on macOS to create .app bundle and .dmg" -ForegroundColor White
Write-Host "   Linux: Run on Linux to create .deb and .rpm packages" -ForegroundColor White
Write-Host ""
