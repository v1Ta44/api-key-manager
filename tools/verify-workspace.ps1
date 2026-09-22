# 新版生产窗口的隔离检查；不读取用户库，不操作系统剪贴板或系统键鼠。
param([string]$ExePath, [string]$OutDir)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $ExePath) {
    & dotnet build (Join-Path $projectRoot 'src\ApiKeyManager.Wpf\ApiKeyManager.Wpf.csproj') --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw '构建失败。' }
    $ExePath = Join-Path $projectRoot 'src\ApiKeyManager.Wpf\bin\Debug\net9.0-windows\ApiKeyManager.exe'
}
if (-not $OutDir) { $OutDir = Join-Path $projectRoot ('artifacts\workspace-' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff')) }
$OutDir = [IO.Path]::GetFullPath($OutDir)
$process = Start-Process -FilePath $ExePath -ArgumentList @('--workspace-check', ('"' + $OutDir + '"')) -PassThru -Wait -WindowStyle Hidden
if ($process.ExitCode -ne 0) { throw "检查失败：$OutDir\checks.json" }
Get-Content (Join-Path $OutDir 'checks.json')
Write-Output "窗口截图：$OutDir"
