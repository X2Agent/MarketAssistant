# WCAG 对比度审计：从 Colors.axaml 解析真实配色并计算对比度。
# 用哈希表而非 PSCustomObject，避免 PowerShell 函数返回时的数组展开歧义。

param(
    [string]$ColorsPath = "src/MarketAssistant.App/Resources/Styles/Colors.axaml"
)

$ErrorActionPreference = 'Stop'

function To-Rgb([string]$hex) {
    $h = $hex.TrimStart('#')
    if ($h.Length -eq 8) {
        $h = $h.Substring(2)   # 丢弃 alpha，本审计对半透明另行处理
    }
    if ($h.Length -ne 6) { throw "无法解析颜色: $hex" }
    return @{
        R = [double][Convert]::ToInt32($h.Substring(0,2),16)
        G = [double][Convert]::ToInt32($h.Substring(2,2),16)
        B = [double][Convert]::ToInt32($h.Substring(4,2),16)
    }
}

function Get-Alpha([string]$hex) {
    $h = $hex.TrimStart('#')
    if ($h.Length -eq 8) { return [double][Convert]::ToInt32($h.Substring(0,2),16) / 255.0 }
    return 1.0
}

function Get-Lum([hashtable]$c) {
    $r = $c.R / 255.0
    $g = $c.G / 255.0
    $b = $c.B / 255.0
    $f = {
        param($v)
        if ($v -le 0.03928) { return $v / 12.92 }
        return [Math]::Pow((($v + 0.055) / 1.055), 2.4)
    }
    $lr = & $f $r
    $lg = & $f $g
    $lb = & $f $b
    return (0.2126 * $lr) + (0.7152 * $lg) + (0.0722 * $lb)
}

# 半透明前景先合成到背景，否则对比度会被高估
function Get-Composite([string]$fgHex, [hashtable]$bg) {
    $alpha = Get-Alpha $fgHex
    $fg = To-Rgb $fgHex
    if ($alpha -ge 0.999) { return $fg }
    return @{
        R = ($fg.R * $alpha) + ($bg.R * (1 - $alpha))
        G = ($fg.G * $alpha) + ($bg.G * (1 - $alpha))
        B = ($fg.B * $alpha) + ($bg.B * (1 - $alpha))
    }
}

function Get-Ratio([string]$fgHex, [string]$bgHex) {
    $bg = To-Rgb $bgHex
    $fg = Get-Composite $fgHex $bg
    $l1 = Get-Lum $fg
    $l2 = Get-Lum $bg
    if ($l1 -lt $l2) { $t = $l1; $l1 = $l2; $l2 = $t }
    return [Math]::Round(($l1 + 0.05) / ($l2 + 0.05), 2)
}

# ---- 解析 Colors.axaml ----
$raw = Get-Content $ColorsPath -Raw

function Get-ThemeBlock([string]$theme) {
    $pattern = "(?s)<ResourceDictionary x:Key=`"$theme`">(.*?)</ResourceDictionary>"
    $m = [regex]::Match($raw, $pattern)
    if (-not $m.Success) { throw "找不到主题块: $theme" }
    return $m.Groups[1].Value
}

function Get-BrushMap([string]$block) {
    $map = @{}
    foreach ($m in [regex]::Matches($block, '<SolidColorBrush x:Key="([^"]+)">\s*(#[0-9A-Fa-f]+)\s*</SolidColorBrush>')) {
        $map[$m.Groups[1].Value] = $m.Groups[2].Value
    }
    return $map
}

$dark  = Get-BrushMap (Get-ThemeBlock 'Dark')
$light = Get-BrushMap (Get-ThemeBlock 'Light')

$globals = @{}
foreach ($m in [regex]::Matches($raw, '<Color x:Key="([^"]+)">(#[0-9A-Fa-f]+)</Color>')) {
    $globals[$m.Groups[1].Value] = $m.Groups[2].Value
}
foreach ($m in [regex]::Matches($raw, '<SolidColorBrush x:Key="(BullishBrush|BearishBrush|NeutralBrush|ErrorBrush|WarningBrush|SuccessBrush)" Color="\{StaticResource ([^}]+)\}"/>')) {
    if ($globals.ContainsKey($m.Groups[2].Value)) { $globals[$m.Groups[1].Value] = $globals[$m.Groups[2].Value] }
}

$failures = New-Object System.Collections.ArrayList

Write-Host "=== 深色模式：文字 x 表面 对比度 ===" -ForegroundColor Cyan
Write-Host "WCAG AA 正文 >= 4.5 / 大字与图形 >= 3.0`n"

$texts = @(
    @('TextPrimaryBrush','主文字'),
    @('TextSecondaryBrush','次文字'),
    @('TextDisabledBrush','弱文字')
)
$surfaces = @(
    @('PageBackgroundBrush','页面'),
    @('CardBackgroundBrush','卡片'),
    @('SurfaceVariantBrush','次级面'),
    @('SunkenBackgroundBrush','凹陷面')
)

foreach ($t in $texts) {
    if (-not $dark.ContainsKey($t[0])) { continue }
    $line = "  {0,-7}" -f $t[1]
    foreach ($s in $surfaces) {
        if (-not $dark.ContainsKey($s[0])) { continue }
        $ratio = Get-Ratio $dark[$t[0]] $dark[$s[0]]
        $mark = if ($ratio -ge 4.5) { 'OK' } elseif ($ratio -ge 3.0) { 'AA*' } else { 'LOW' }
        if ($ratio -lt 4.5 -and $t[0] -ne 'TextDisabledBrush') {
            [void]$failures.Add("暗色 $($t[1]) on $($s[1]) = $ratio")
        }
        $line += " {0}={1}({2}) " -f $s[1], $ratio, $mark
    }
    Write-Host $line
}

Write-Host "`n=== 深色模式：语义色 on 卡片 / 页面 ===" -ForegroundColor Cyan
foreach ($k in @('BullishBrush','BearishBrush','NeutralBrush','ErrorBrush','WarningBrush','SuccessBrush')) {
    if (-not $globals.ContainsKey($k)) { continue }
    $onCard = Get-Ratio $globals[$k] $dark['CardBackgroundBrush']
    $onPage = Get-Ratio $globals[$k] $dark['PageBackgroundBrush']
    $mark = if ($onCard -ge 4.5) { 'OK' } elseif ($onCard -ge 3.0) { 'AA*大字/图形' } else { 'LOW' }
    if ($onCard -lt 4.5) { [void]$failures.Add("暗色 $k on 卡片 = $onCard") }
    Write-Host ("  {0,-16} 卡片={1,-6} 页面={2,-6} {3}" -f $k, $onCard, $onPage, $mark)
}

Write-Host "`n=== 深色模式：语义面板文字 on 面板底 ===" -ForegroundColor Cyan
foreach ($p in @(
    @('SuccessPanelTextBrush','SuccessPanelBackgroundBrush','成功面板'),
    @('WarningPanelTextBrush','WarningPanelBackgroundBrush','警告面板'),
    @('DangerPanelTextBrush','DangerPanelBackgroundBrush','危险面板'),
    @('InfoPanelTextBrush','InfoPanelBackgroundBrush','信息面板')
)) {
    if (-not $dark.ContainsKey($p[0]) -or -not $dark.ContainsKey($p[1])) { continue }
    $ratio = Get-Ratio $dark[$p[0]] $dark[$p[1]]
    $mark = if ($ratio -ge 4.5) { 'OK' } elseif ($ratio -ge 3.0) { 'AA*' } else { 'LOW' }
    if ($ratio -lt 4.5) { [void]$failures.Add("暗色 $($p[2]) = $ratio") }
    Write-Host ("  {0,-10} = {1,-6} {2}" -f $p[2], $ratio, $mark)
}

Write-Host "`n=== 浅色模式对照 ===" -ForegroundColor Cyan
foreach ($t in @(@('TextPrimaryBrush','主文字'), @('TextSecondaryBrush','次文字'))) {
    if (-not $light.ContainsKey($t[0])) { continue }
    $ratio = Get-Ratio $light[$t[0]] $light['CardBackgroundBrush']
    $mark = if ($ratio -ge 4.5) { 'OK' } elseif ($ratio -ge 3.0) { 'AA*' } else { 'LOW' }
    if ($ratio -lt 4.5) { [void]$failures.Add("浅色 $($t[1]) on 卡片 = $ratio") }
    Write-Host ("  {0,-7} on 卡片 = {1,-6} {2}" -f $t[1], $ratio, $mark)
}

Write-Host ""
if ($failures.Count -eq 0) {
    Write-Host "全部达标（>= 4.5）" -ForegroundColor Green
} else {
    Write-Host "低于 AA 4.5 的组合（$($failures.Count) 项）：" -ForegroundColor Yellow
    $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
}
