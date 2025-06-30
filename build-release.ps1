# MarketAssistant Build Script
# Build Windows and macOS versions locally

param(
    [string]$Platform = "All",  # All, Windows, macOS
    [string]$Configuration = "Release"
)

Write-Host "Starting MarketAssistant build..." -ForegroundColor Green

# Check .NET and MAUI workload
Write-Host "Checking .NET environment..." -ForegroundColor Yellow
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
dotnet restore MarketAssistant.sln

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
        dotnet publish MarketAssistant/MarketAssistant.WinUI/MarketAssistant.WinUI.csproj `
            -c $Configuration `
            -f net9.0-windows10.0.19041.0 `
            -p:Platform=x64 `
            -p:PublishSingleFile=true `
            -p:SelfContained=true `
            -p:RuntimeIdentifier=win-x64 `
            -o "$outputDir/Windows"
            
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✓ Windows build successful" -ForegroundColor Green
            
            # Create ZIP package
            $zipPath = "$outputDir/MarketAssistant-Windows-x64.zip"
            Compress-Archive -Path "$outputDir/Windows/*" -DestinationPath $zipPath -Force
            Write-Host "✓ Windows ZIP created: $zipPath" -ForegroundColor Green
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

Write-Host "Build completed! Output directory: $outputDir" -ForegroundColor Green
Write-Host "`nUsage:" -ForegroundColor Cyan