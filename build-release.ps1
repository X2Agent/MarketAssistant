# MarketAssistant Build Script v2.0
# Build optimized Windows and macOS versions locally
# Features: Single-file builds, size optimization, automatic cleanup

param(
    [string]$Platform = "All",  # All, Windows, macOS
    [string]$Configuration = "Release"
)

Write-Host "🚀 MarketAssistant Build Script v2.0" -ForegroundColor Green
Write-Host "Platform: $Platform | Configuration: $Configuration" -ForegroundColor Cyan

# Check .NET and MAUI workload
Write-Host "🔍 Checking prerequisites..." -ForegroundColor Yellow
try {
    $dotnetVersion = dotnet --version
    Write-Host "✓ .NET version: $dotnetVersion" -ForegroundColor Green
} catch {
    Write-Host "✗ .NET SDK not found" -ForegroundColor Red
    exit 1
}

# Install MAUI workload (Windows and macOS only)
Write-Host "Installing MAUI workload for Windows and macOS..." -ForegroundColor Yellow
dotnet workload install maui-windows maui-maccatalyst

# Restore dependencies
Write-Host "Restoring project dependencies..." -ForegroundColor Yellow
dotnet restore MarketAssistant.slnx

# Create output directory
$outputDir = "./Release"
if (Test-Path $outputDir) {
    Remove-Item $outputDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

# Build Windows version
if ($Platform -eq "All" -or $Platform -eq "Windows") {
    Write-Host "Building Windows version..." -ForegroundColor Yellow
    
    try {
        # 使用框架依赖部署（推荐用于 WinUI 3 应用）
        dotnet publish MarketAssistant/MarketAssistant.WinUI/MarketAssistant.WinUI.csproj `
             -c $Configuration `
             -f net9.0-windows10.0.19041.0 `
             -p:Platform=x64 `
             -p:SelfContained=false `
             -p:PublishReadyToRun=true `
             -o "$outputDir/Windows"
            
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✓ Windows build successful" -ForegroundColor Green
            
            # 📊 显示构建前的大小
            $buildSize = (Get-ChildItem -Path "$outputDir/Windows" -Recurse | Measure-Object -Property Length -Sum).Sum
            Write-Host "🔍 Build size before optimization: $([math]::Round($buildSize/1MB, 2)) MB" -ForegroundColor Yellow
            
            # 🗑️ 清理不必要的文件来减少大小
            Write-Host "🧹 Optimizing single-file build..." -ForegroundColor Yellow
            $removedItems = 0
            $savedSize = 0
            
            # 删除调试文件
            $debugFiles = @("*.pdb", "*.xml", "*.deps.json")
            foreach ($pattern in $debugFiles) {
                $files = Get-ChildItem -Path "$outputDir/Windows" -Filter $pattern -Recurse
                foreach ($file in $files) {
                    $savedSize += $file.Length
                    Remove-Item $file.FullName -Force
                    $removedItems++
                }
            }
            
            # 检查是否有单个可执行文件
            $exeFile = Get-ChildItem -Path "$outputDir/Windows" -Filter "*.exe" | Select-Object -First 1
            if ($exeFile) {
                Write-Host "   ✓ Single executable: $($exeFile.Name) ($([math]::Round($exeFile.Length/1MB, 2)) MB)" -ForegroundColor Green
            }
            
            # Create ZIP package
            Write-Host "📦 Creating ZIP package..." -ForegroundColor Yellow
            $zipPath = "$outputDir/MarketAssistant-Windows-x64.zip"
            Compress-Archive -Path "$outputDir/Windows/*" -DestinationPath $zipPath -Force
            $zipSize = (Get-Item $zipPath).Length
            Write-Host "✓ Windows ZIP created: ./Release/MarketAssistant-Windows-x64.zip" -ForegroundColor Green
            Write-Host "   ZIP size: $([math]::Round($zipSize/1MB, 2)) MB" -ForegroundColor Green
        } else {
            Write-Host "✗ Windows build failed" -ForegroundColor Red
        }
    } catch {
        Write-Host "✗ Windows build error: $($_.Exception.Message)" -ForegroundColor Red
    }
}

# Build macOS version (macOS only)
if ($Platform -eq "All" -or $Platform -eq "macOS") {
    if ($IsMacOS) {
        Write-Host "Building macOS version..." -ForegroundColor Yellow
        
        try {
            dotnet publish MarketAssistant/MarketAssistant.Mac/MarketAssistant.Mac.csproj `
                -c $Configuration `
                -f net9.0-maccatalyst `
                -p:CreatePackage=true `
                -o "$outputDir/macOS"
                
            if ($LASTEXITCODE -eq 0) {
                Write-Host "✓ macOS build successful" -ForegroundColor Green
                
                # Find .app file and create DMG
                $appPath = Get-ChildItem -Path "$outputDir/macOS" -Filter "*.app" -Directory | Select-Object -First 1
                
                if ($appPath) {
                    Write-Host "Found app bundle: $($appPath.FullName)" -ForegroundColor Green
                    
                    # Create DMG (requires macOS)
                    $dmgPath = "$outputDir/MarketAssistant-macOS.dmg"
                    hdiutil create -volname "MarketAssistant" -srcfolder $appPath.FullName -ov -format UDZO $dmgPath
                    
                    if ($LASTEXITCODE -eq 0) {
                        Write-Host "✓ macOS DMG created: $dmgPath" -ForegroundColor Green
                    }
                } else {
                    Write-Host "App bundle not found, creating ZIP..." -ForegroundColor Yellow
                    $zipPath = "$outputDir/MarketAssistant-macOS.zip"
                    Compress-Archive -Path "$outputDir/macOS/*" -DestinationPath $zipPath -Force
                    Write-Host "✓ macOS ZIP created: $zipPath" -ForegroundColor Green
                }
            } else {
                Write-Host "✗ macOS build failed" -ForegroundColor Red
            }
        } catch {
            Write-Host "✗ macOS build error: $($_.Exception.Message)" -ForegroundColor Red
        }
    } else {
        Write-Host "⚠ macOS version can only be built on macOS" -ForegroundColor Yellow
    }
}

Write-Host "🎉 Build process completed!" -ForegroundColor Green
Write-Host "📁 Output directory: $outputDir" -ForegroundColor Cyan

# 📊 显示最终文件统计
if (Test-Path "$outputDir") {
    Write-Host "📋 Generated files:" -ForegroundColor Cyan
    $allFiles = Get-ChildItem -Path "$outputDir" -File -Recurse
    $totalSize = 0
    foreach ($file in $allFiles) {
        if ($file.Extension -eq ".zip" -or $file.Extension -eq ".exe" -or $file.Extension -eq ".dmg") {
            Write-Host "   📦 $($file.Name) ($([math]::Round($file.Length/1MB, 2)) MB)" -ForegroundColor Yellow
            $totalSize += $file.Length
        }
    }
    Write-Host "📊 Total output size: $([math]::Round($totalSize/1MB, 2)) MB" -ForegroundColor Green
}

Write-Host "`n📈 Build Summary:" -ForegroundColor Cyan
Write-Host "   Platform: $Platform" -ForegroundColor White
Write-Host "   Configuration: $Configuration" -ForegroundColor White
Write-Host "   Build Time: $(Get-Date -Format 'MM/dd/yyyy HH:mm:ss')" -ForegroundColor White

Write-Host "`nUsage:" -ForegroundColor Cyan