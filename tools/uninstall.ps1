# Удаление установленного CoveyPlanner: останавливает сервер, убирает задачу из Планировщика заданий
# и папку программы. База и резервные копии НЕ удаляются.
#
#   powershell -File tools/uninstall.ps1
param(
    [string]$TaskName = 'CoveyPlanner',
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'CoveyPlanner\app')
)

$ErrorActionPreference = 'Stop'

if (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue) {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    Write-Host "Задача «$TaskName» удалена из Планировщика заданий."
}
else {
    Write-Host "Задачи «$TaskName» нет."
}

Get-Process Planner.Server -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($InstallDir, [StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object { Stop-Process -Id $_.Id -Force; $_.WaitForExit(5000) | Out-Null }

if (Test-Path $InstallDir) {
    Remove-Item $InstallDir -Recurse -Force
    Write-Host "Папка программы удалена: $InstallDir"
}

$data = Split-Path $InstallDir -Parent
Write-Host "Данные не тронуты: $data (planner.db, backups)"
