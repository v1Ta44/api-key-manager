# 旧界面坐标脚本已退役；请使用生产窗口的隔离控件检查。
param([string]$ExePath, [string]$OutDir, [string]$Password, [string[]]$AppArgs)
Write-Warning '此入口已迁移为 --workspace-check；它不再驱动系统键鼠。Password/AppArgs 不会传入应用。'
& (Join-Path $PSScriptRoot 'verify-workspace.ps1') -ExePath $ExePath -OutDir $OutDir
