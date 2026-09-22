# 运行正式新版的隔离演示；-Prototype 可回看第一阶段原型。
param([switch]$Check, [switch]$Prototype)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& dotnet build (Join-Path $projectRoot 'src\ApiKeyManager.Wpf\ApiKeyManager.Wpf.csproj') --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw '构建失败。' }
$exe = Join-Path $projectRoot 'src\ApiKeyManager.Wpf\bin\Debug\net9.0-windows\ApiKeyManager.exe'
if ($Check) {
    if ($Prototype) {
        $output = Join-Path $projectRoot 'artifacts\prototype-review'
        $process = Start-Process -FilePath $exe -ArgumentList @('--prototype-check', ('"' + $output + '"')) -PassThru -Wait -WindowStyle Hidden
        if ($process.ExitCode -ne 0) { throw "原型检查失败：$output" }
    } else { & (Join-Path $PSScriptRoot 'verify-workspace.ps1') -ExePath $exe }
} else {
    $mode = if ($Prototype) { '--prototype' } else { '--demo' }
    # 用户运行预览脚本，是为了亲自操作这个窗口。
    Start-Process -FilePath $exe -ArgumentList $mode -WindowStyle Normal | Out-Null
}
