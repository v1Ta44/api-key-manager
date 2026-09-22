# 本地打包与验证。每次使用新目录，不删除旧产物，不终止用户进程。
param([string]$Configuration = 'Release', [string]$OutRoot = 'artifacts\publish', [string]$DotnetPath = 'dotnet')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'src\ApiKeyManager.Wpf\ApiKeyManager.Wpf.csproj'
$outputRoot = if ([IO.Path]::IsPathRooted($OutRoot)) { $OutRoot } else { Join-Path $projectRoot $OutRoot }
$run = Join-Path ([IO.Path]::GetFullPath($outputRoot)) (Get-Date -Format 'yyyyMMdd-HHmmss-ffff')
New-Item -ItemType Directory -Path $run | Out-Null
$results = @()
function Invoke-CheckedProcess([string]$File, [string[]]$Arguments) {
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -PassThru -WindowStyle Hidden
    if (-not $process.WaitForExit(120000)) {
        # 只终止本次启动且超时的检查器。
        Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
        throw "检查超时：$File"
    }
    $process.Refresh()
    if ($process.ExitCode -ne 0) { throw "检查失败（$($process.ExitCode)）：$File $Arguments" }
}
foreach ($kind in @('fd', 'sc')) {
    $out = Join-Path $run $kind
    $selfContained = ($kind -eq 'sc').ToString().ToLowerInvariant()
    # 单文件选项已在 WPF 项目中声明，不作为全局参数传入纯类库，避免改变它们的锁定依赖图。
    & $DotnetPath publish $project -c $Configuration -r win-x64 --self-contained $selfContained -p:RestoreLockedMode=true -p:DebugType=none -o $out --nologo -v minimal -warnaserror
    if ($LASTEXITCODE -ne 0) { throw "打包失败：$kind" }
    $exe = Join-Path $out 'ApiKeyManager.exe'
    Invoke-CheckedProcess $exe @('--selftest')
    $checkDir = Join-Path $run ($kind + '-checks')
    Invoke-CheckedProcess $exe @('--workspace-check', ('"' + $checkDir + '"'))
    $checks = Get-Content (Join-Path $checkDir 'checks.json') -Raw | ConvertFrom-Json
    if ($checks.failure -or $checks.passed -lt 79) { throw "窗口检查报告不完整：$checkDir" }
    $files = Get-ChildItem -LiteralPath $out -File
    $results += [pscustomobject]@{ Kind = $kind; Directory = $out; Files = $files.Count; Bytes = ($files | Measure-Object Length -Sum).Sum; SelfTest = 'passed'; WorkspaceCheck = 'passed'; CheckCount = $checks.passed; Runtime = $checks.runtime; Sha256 = (Get-FileHash $exe -Algorithm SHA256).Hash }
}
$results | ConvertTo-Json | Set-Content (Join-Path $run 'publish-results.json') -Encoding utf8
$results | Format-Table -AutoSize
Write-Output "本地产物与验证记录：$run"
