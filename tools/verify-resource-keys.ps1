# 资源键完整性校验：所有 {StaticResource X} / {DynamicResource X} 的 X 是否都有定义。
# XAML 对未定义键不报编译错误，只会在运行时静默失效（控件失去样式），
# 因此清理兼容层后必须用脚本对账。
#
# 覆盖面（缺任何一类都会漏掉真实故障）：
#   1. XAML 里的 {StaticResource X} / {DynamicResource X}
#   2. C# 里的字符串键 —— FindResource("X") / TryGetResource("X") /
#      TryFindResource("X") / DynamicResourceExtension("X")
#      这是最容易漏的一类：XAML 编译期会校验 StaticResource，C# 字符串完全不会。
#      典型事故：v3 删掉 v2 的 PrimaryBrush 后，LoadingDots 仍按字符串取它，
#      圆点 Fill 变成 null，整个加载指示器不可见，而所有 XAML 都"检查通过"。
#
# 间接引用（转换器/代码按 key 名拼字符串）不在本脚本的静态分析范围内，
# 这类令牌会在下面的"疑似未引用"清单里单独标注，删之前请人工确认。

param(
    [string]$Root = "src/MarketAssistant.App"
)

$ErrorActionPreference = 'Stop'

function Get-Files([string]$dir, [string]$filter) {
    Get-ChildItem -Path $dir -Recurse -Include $filter -File |
        Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\' }
}

# App.axaml 在项目根目录，不在 Resources/ 或 Views/ 下。漏掉它会把
# UiFontFamily / ToggleSwitchStyles 等"只在根文件引用"的键误报为未使用。
$axaml = Get-Files $Root '*.axaml'
$cs = Get-Files $Root '*.cs'

Push-Location .
Set-Location $Root

# ---- 收集定义 ----
$defined = @{}
$allFiles = @($axaml) + @($cs)
foreach ($f in $allFiles) {
    $content = Get-Content $f.FullName -Raw -ErrorAction SilentlyContinue
    if (-not $content) { continue }
    foreach ($m in [regex]::Matches($content, 'x:Key\s*=\s*"([^"]+)"')) {
        $defined[$m.Groups[1].Value] = $f.Name
    }
}

# ---- 收集引用（XAML 标记扩展） ----
$refs = @{}
foreach ($f in @($axaml)) {
    $content = Get-Content $f.FullName -Raw
    foreach ($m in [regex]::Matches($content, '\{(?:StaticResource|DynamicResource)\s+([A-Za-z0-9_]+)\s*\}')) {
        $key = $m.Groups[1].Value
        if (-not $refs.ContainsKey($key)) { $refs[$key] = @() }
        $refs[$key] += $f.Name
    }
}

# ---- 收集引用（C# 字符串键） ----
# 只匹配"整串就是一个资源键"的调用形态，避免把日志文案当资源键。
# 注释先剔除：注释里提到的历史键名不代表真实引用。
#
# 两档处理：
#   [C# 直接] —— FindResource / TryGetResource / DynamicResourceExtension 等 API 的键参数
#   [C# 间接] —— 任意字符串字面量，只要它恰好等于某个已定义的键
#                （覆盖 MotionTokens 的 Get("X")、MainWindow 的 ReadDouble("X")、
#                 转换器里的三元 "BullishTagBackgroundBrush" / "BullishBrush" 等）
$csKeyPattern = '(?:FindResource|TryGetResource|TryFindResource|DynamicResourceExtension)\s*\(\s*"([A-Za-z0-9_]+)"'
$csLiteralPattern = '"([A-Za-z0-9_]{4,})"'
foreach ($f in @($cs)) {
    $content = Get-Content $f.FullName -Raw -ErrorAction SilentlyContinue
    if (-not $content) { continue }
    $content = [regex]::Replace($content, '(?s)/\*.*?\*/', '')
    $content = [regex]::Replace($content, '(?m)//.*$', '')

    $direct = @{}
    foreach ($m in [regex]::Matches($content, $csKeyPattern)) {
        $direct[$m.Groups[1].Value] = $true
        $key = $m.Groups[1].Value
        if (-not $refs.ContainsKey($key)) { $refs[$key] = @() }
        $refs[$key] += "$($f.Name) [C#]"
    }

    foreach ($m in [regex]::Matches($content, $csLiteralPattern)) {
        $key = $m.Groups[1].Value
        if ($direct.ContainsKey($key) -or -not $defined.ContainsKey($key)) { continue }
        if (-not $refs.ContainsKey($key)) { $refs[$key] = @() }
        $refs[$key] += "$($f.Name) [C# 间接]"
    }
}

Pop-Location

Write-Host "定义键 $($defined.Count) 个，引用键 $($refs.Count) 个`n"

# ---- 对账 ----
$missing = @()
foreach ($k in $refs.Keys | Sort-Object) {
    if (-not $defined.ContainsKey($k)) {
        $files = ($refs[$k] | Sort-Object -Unique) -join ', '
        $hasCodeRef = @($refs[$k] | Where-Object { $_ -like '*[C#]*' }).Count -gt 0
        $missing += [pscustomobject]@{ Key = $k; Refs = $refs[$k].Count; Files = $files; Code = $hasCodeRef }
    }
}

if ($missing.Count -eq 0) {
    Write-Host "全部引用键都有定义（无静默失效风险）" -ForegroundColor Green
}
else {
    Write-Host "未定义的引用键（运行时静默失效）：" -ForegroundColor Red
    $missing | ForEach-Object {
        $tag = if ($_.Code) { " [C# 字符串键]" } else { "" }
        Write-Host ("  {0,-32} 引用 {1,2} 处  于 {2}{3}" -f $_.Key, $_.Refs, $_.Files, $tag) -ForegroundColor Red
    }
    exit 1
}

# ---- 提示：定义了但没有任何引用的键（含转换器间接引用，需人工确认后再删） ----
$unused = @()
foreach ($k in $defined.Keys | Sort-Object) {
    if (-not $refs.ContainsKey($k)) { $unused += $k }
}

if ($unused.Count -gt 0) {
    Write-Host "`n未直接引用的定义（可能由代码按字符串间接引用，删除前请确认）：" -ForegroundColor DarkYellow
    foreach ($k in $unused) {
        Write-Host ("  {0,-32} 定义于 {1}" -f $k, $defined[$k]) -ForegroundColor DarkYellow
    }
}
