# MarketAssistant Build Script - Avalonia
# 跨平台构建脚本：Windows, macOS, Linux

param(
    [string]$Platform = "Windows",  # Windows, macOS, Linux, All
    [string]$Configuration = "Release"
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

# 构建函数
function Build-Package {
    param([string]$Name, [string]$Runtime)
    
    Write-Host "`n🔨 Building $Name..." -ForegroundColor Cyan
    
    $publishDir = "$outputDir/$Name"
    $buildStart = Get-Date
    
    dotnet publish src/MarketAssistant.csproj `
        -c $Configuration `
        -r $Runtime `
        --self-contained `
        -p:PublishReadyToRun=true `
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
    $zipPath = "$outputDir/MarketAssistant-$Name.zip"
    Compress-Archive -Path "$publishDir/*" -DestinationPath $zipPath -CompressionLevel Optimal -Force
    
    $zipSize = (Get-Item $zipPath).Length
    
    Write-Host "   Build size: $([math]::Round($beforeSize/1MB, 2)) MB" -ForegroundColor Gray
    Write-Host "   Cleaned: $([math]::Round($cleanedSize/1MB, 2)) MB (debug files)" -ForegroundColor Gray
    Write-Host "   Final size: $([math]::Round($afterSize/1MB, 2)) MB" -ForegroundColor Gray
    Write-Host "   ZIP size: $([math]::Round($zipSize/1MB, 2)) MB" -ForegroundColor Yellow
    Write-Host "   Build time: $([math]::Round($buildTime, 1))s" -ForegroundColor Gray
    Write-Host "✓ $Name completed" -ForegroundColor Green
    
    return @{
        Name = $Name
        ZipPath = $zipPath
        ZipSize = $zipSize
        BuildSize = $afterSize
        BuildTime = $buildTime
    }
}

# 执行构建
$results = @()

if ($Platform -eq "All" -or $Platform -eq "Windows") {
    $result = Build-Package "Windows-x64" "win-x64"
    if ($result) { $results += $result }
}

if ($Platform -eq "All" -or $Platform -eq "macOS") {
    if ($IsMacOS -or $Platform -eq "All") {
        $result = Build-Package "macOS-x64" "osx-x64"
        if ($result) {
            $results += $result
            
            # 在 macOS 上创建 DMG
            if ($IsMacOS) {
                Write-Host "`n📀 Creating DMG package..." -ForegroundColor Cyan
                $appDir = "$outputDir/temp/MarketAssistant.app/Contents/MacOS"
                New-Item -ItemType Directory -Force -Path $appDir | Out-Null
                Copy-Item -Path "$outputDir/macOS-x64/*" -Destination $appDir -Recurse
                
                hdiutil create -volname "MarketAssistant" `
                    -srcfolder "$outputDir/temp/MarketAssistant.app" `
                    -ov -format UDZO `
                    "$outputDir/MarketAssistant-macOS.dmg" 2>&1 | Out-Null
                
                if ($LASTEXITCODE -eq 0) {
                    $dmgSize = (Get-Item "$outputDir/MarketAssistant-macOS.dmg").Length
                    Write-Host "✓ DMG created: $([math]::Round($dmgSize/1MB, 2)) MB" -ForegroundColor Green
                    Remove-Item "$outputDir/temp" -Recurse -Force
                }
            }
        }
    }
}

if ($Platform -eq "All" -or $Platform -eq "Linux") {
    $result = Build-Package "Linux-x64" "linux-x64"
    if ($result) { $results += $result }
}

$totalTime = ((Get-Date) - $startTime).TotalSeconds

# 构建汇总
Write-Host "`n" + "="*60 -ForegroundColor Cyan
Write-Host "📊 Build Summary" -ForegroundColor Cyan
Write-Host "="*60 -ForegroundColor Cyan

if ($results.Count -gt 0) {
    Write-Host "`n📦 Generated Packages:" -ForegroundColor Yellow
    $totalZipSize = 0
    foreach ($result in $results) {
        Write-Host "   • $($result.Name).zip - $([math]::Round($result.ZipSize/1MB, 2)) MB" -ForegroundColor White
        $totalZipSize += $result.ZipSize
    }
    
    # 如果有 DMG 文件
    $dmgFile = Get-ChildItem -Path "$outputDir" -Filter "*.dmg" -ErrorAction SilentlyContinue
    if ($dmgFile) {
        Write-Host "   • macOS.dmg - $([math]::Round($dmgFile.Length/1MB, 2)) MB" -ForegroundColor White
        $totalZipSize += $dmgFile.Length
    }
    
    Write-Host "`n📈 Statistics:" -ForegroundColor Yellow
    Write-Host "   Platforms built: $($results.Count)" -ForegroundColor White
    Write-Host "   Total package size: $([math]::Round($totalZipSize/1MB, 2)) MB" -ForegroundColor White
    Write-Host "   Total build time: $([math]::Round($totalTime, 1))s" -ForegroundColor White
    Write-Host "   Output directory: $outputDir" -ForegroundColor White
    
    Write-Host "`n✅ Build completed successfully!" -ForegroundColor Green
} else {
    Write-Host "`n❌ No packages were built" -ForegroundColor Red
}

Write-Host "`n" + "="*60 -ForegroundColor Cyan
Write-Host "📖 Usage Examples" -ForegroundColor Cyan
Write-Host "="*60 -ForegroundColor Cyan
Write-Host "   .\build-release.ps1                     # Build Windows (default)" -ForegroundColor White
Write-Host "   .\build-release.ps1 -Platform macOS     # Build macOS" -ForegroundColor White
Write-Host "   .\build-release.ps1 -Platform Linux     # Build Linux" -ForegroundColor White
Write-Host "   .\build-release.ps1 -Platform All       # Build all platforms" -ForegroundColor White
Write-Host ""
