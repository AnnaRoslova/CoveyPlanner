# Резервная копия базы CoveyPlanner — сейчас, вручную.
# (Сервер и сам делает копию раз в день при запуске и перед обновлением схемы базы.)
#
#   powershell -File tools/backup.ps1
#   powershell -File tools/backup.ps1 -DatabasePath D:\Planner\planner.db   — если путь к базе изменён в appsettings.json
#
# Сервер запущен — копию делает он (целостная копия средствами SQLite, работу останавливать не нужно).
# Сервер остановлен — скрипт просто копирует файл базы в папку backups рядом с ней.
param(
    [string]$Url = 'http://localhost:5070',
    [string]$DatabasePath = (Join-Path $env:LOCALAPPDATA 'CoveyPlanner\planner.db')
)

$ErrorActionPreference = 'Stop'

try {
    $result = Invoke-RestMethod -Method Post -Uri "$Url/api/backup" -TimeoutSec 30
    Write-Host "Копию сделал сервер: $($result.path)"
    if ($result.path -like "$env:TEMP*") {
        Write-Host 'Внимание: на этом адресе запущено демо — это копия демо-базы, а не основной.' -ForegroundColor Yellow
    }
    exit 0
}
catch [System.Net.WebException] {
    # Сервер не отвечает — копируем файл сами
}

if (-not (Test-Path $DatabasePath)) {
    throw "Сервер не запущен, а файла базы нет: $DatabasePath"
}

$backups = Join-Path (Split-Path $DatabasePath -Parent) 'backups'
New-Item -ItemType Directory -Force $backups | Out-Null
$target = Join-Path $backups ("planner-{0:yyyy-MM-dd_HHmmss}-вручную.db" -f (Get-Date))
Copy-Item $DatabasePath $target

# После аварийного завершения часть изменений может лежать в журнале -wal — копируем и его
if (Test-Path "$DatabasePath-wal") {
    Copy-Item "$DatabasePath-wal" "$target-wal"
    Write-Host 'Скопирован и журнал (-wal): при восстановлении положите оба файла рядом.' -ForegroundColor Yellow
}

Write-Host "Сервер не запущен — файл базы скопирован: $target"
