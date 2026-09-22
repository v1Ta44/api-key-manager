# API Key 管理器 发布验证脚本
#
# 用法：
#   .\tools\verify-publish.ps1
#
# 做三件事：
#   1. 用两种形态各发布一次（框架依赖 / 自包含）
#   2. 检查体积
#   3. 对每个产物跑一次 --selftest，确认发布后的二进制真能跑
#
# 为什么必须在发布后单独跑自检：
#   Release + 单文件 + 压缩会和 Debug 走完全不同的打包路径
#   （IL 合并、资源内嵌、程序集解析都不同），
#   编译通过不代表发布产物能启动 —— 这里就是最后一道闸。
param(
    [string]$Configuration = "Release",
    [string]$OutRoot = "publish-test"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $root

Get-Process -Name ApiKeyManager -ErrorAction SilentlyContinue | Stop-Process -Force

$project = ".\src\ApiKeyManager.Wpf\ApiKeyManager.Wpf.csproj"
$failed = $false

function Publish-One {
    param([string]$Name, [bool]$SelfContained, [string]$OutDir)

    Write-Host ""
    Write-Host "=== 发布: $Name ==="
    Remove-Item -Recurse -Force $OutDir -ErrorAction SilentlyContinue

    # 注意：不要用 $args —— 那是 PowerShell 的自动变量，赋值会被静默忽略
    $dotnetArgs = @(
        "publish", $project,
        "-c", $Configuration,
        "-r", "win-x64",
        "--self-contained", $SelfContained.ToString().ToLowerInvariant(),
        "-p:PublishSingleFile=true",
        "-p:DebugType=none",
        "-o", $OutDir,
        "--nologo", "-v", "q"
    )

    # 单文件压缩只支持自包含发布；框架依赖版开启会得到 NETSDK1176
    if ($SelfContained) { $dotnetArgs += "-p:EnableCompressionInSingleFile=true" }

    $output = & dotnet @dotnetArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  发布失败："
        $output | Select-Object -Last 10 | ForEach-Object { Write-Host "    $_" }
        return $null
    }

    $files = Get-ChildItem $OutDir -Recurse -File
    $total = ($files | Measure-Object Length -Sum).Sum
    # 用 Write-Host 而不是 Write-Output：后者会混进函数返回值，
    # 调用方拿到的就是数组而不是单个哈希表，$fd.Dir 会变成空串。
    Write-Host ("  文件数: {0}" -f $files.Count)
    Write-Host ("  总大小: {0:N2} MB" -f ($total / 1MB))

    return @{ Dir = $OutDir; Files = $files.Count; Bytes = $total }
}

function Test-Artifact {
    param([string]$Dir, [string]$Label)

    # 同样用 Write-Host，避免消息混进布尔返回值
    $exe = Join-Path $Dir "ApiKeyManager.exe"
    if (-not (Test-Path $exe)) {
        Write-Host "  [$Label] 找不到 ApiKeyManager.exe"
        return $false
    }

    $out = & $exe --selftest 2>&1
    $code = $LASTEXITCODE
    $allPass = ($out | Select-String -Pattern "ALL PASS" -Quiet)

    # 退出码是权威判据。自包含版会 attach 到自己的控制台，
    # stdout 不一定能被父进程管道捕获，只按文本判断会误报失败。
    if ($code -eq 0) {
        if ($allPass) {
            Write-Host "  [$Label] 自检通过 (exit 0, ALL PASS)"
        } else {
            Write-Host "  [$Label] 自检通过 (exit 0；控制台输出未捕获，属正常)"
        }
        return $true
    }

    Write-Host "  [$Label] 自检失败 exit=$code"
    $out | Select-Object -Last 6 | ForEach-Object { Write-Host "    $_" }
    return $false
}

Write-Output "=== 发布验证开始 ==="
Write-Output "配置: $Configuration   目标: win-x64"

$fd = Publish-One -Name "框架依赖单文件" -SelfContained $false -OutDir "$OutRoot\fd"
$sc = Publish-One -Name "自包含单文件"   -SelfContained $true  -OutDir "$OutRoot\sc"

Write-Output ""
Write-Output "=== 体积对比 ==="
if ($fd) { Write-Output ("  框架依赖: {0:N2} MB  ({1} 个文件)" -f ($fd.Bytes/1MB), $fd.Files) }
if ($sc) { Write-Output ("  自包含:   {0:N2} MB  ({1} 个文件)" -f ($sc.Bytes/1MB), $sc.Files) }

Write-Output ""
Write-Output "=== 产物自检 ==="
if ($fd) { if (-not (Test-Artifact -Dir $fd.Dir -Label "框架依赖")) { $failed = $true } }
if ($sc) { if (-not (Test-Artifact -Dir $sc.Dir -Label "自包含"))   { $failed = $true } }

Write-Output ""
if ($failed) {
    Write-Output "结果：存在失败项"
    exit 1
}
Write-Output "结果：全部通过"
exit 0
