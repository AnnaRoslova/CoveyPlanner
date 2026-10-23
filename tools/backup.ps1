# Резервная копия CoveyPlanner — сейчас, вручную: снимок папкой со всем, что нужно для восстановления
# (docs/specs/accounts.md, D5): accounts.db, базы всех пользователей (users\*.db), ключи (keys\), local.key, settings.json.
# (Сервер и сам делает снимок раз в день при запуске и перед обновлением схемы.)
#
#   powershell -File tools/backup.ps1
#   powershell -File tools/backup.ps1 -DataDirectory D:\Planner   — если папка данных изменена в appsettings.json
#
# Сервер запущен — снимок делает он (базы — целостно, средствами SQLite, работу останавливать не нужно);
# скрипт показывает ему ключ из local.key (копию может попросить только тот, у кого есть доступ к диску сервера).
# Сервер остановлен — скрипт сам копирует те же файлы в backups\planner-…-вручную (базы — вместе с журналом -wal).
param(
    [string]$Url = 'http://localhost:5070',
    [string]$DataDirectory = (Join-Path $env:LOCALAPPDATA 'CoveyPlanner')
)

$ErrorActionPreference = 'Stop'
$keyFile = Join-Path $DataDirectory 'local.key'
$headers = @{}
if (Test-Path $keyFile) { $headers['X-Planner-Local-Key'] = (Get-Content $keyFile -Raw).Trim() }

try {
    $result = Invoke-RestMethod -Method Post -Uri "$Url/api/backup" -Headers $headers -TimeoutSec 60
    Write-Host "Копию сделал сервер: $($result.path)"
    if ($result.path -like "$env:TEMP*") {
        Write-Host 'Внимание: на этом адресе запущено демо — это копия демо-данных, а не основных.' -ForegroundColor Yellow
    }
    exit 0
}
catch [System.Net.WebException] {
    # Сервер ответил ошибкой (например, 403 — ключ не тот) — не копируем файлы работающего сервера сами
    if ($_.Exception.Response) { throw "Сервер не сделал копию: $($_.Exception.Message)" }
    # Сервер не отвечает — копируем файлы сами
}

# @(…) снаружи: одна найденная база — всё равно список, а не строка (иначе += склеит пути)
$databases = @(@(Join-Path $DataDirectory 'accounts.db'; Join-Path $DataDirectory 'planner.db') | Where-Object { Test-Path $_ })
$users = Join-Path $DataDirectory 'users'
if (Test-Path $users) { $databases += @(Get-ChildItem $users -Filter '*.db' | ForEach-Object FullName) }
if ($databases.Count -eq 0) { throw "Сервер не запущен, а баз в папке данных нет: $DataDirectory" }

$target = Join-Path $DataDirectory ("backups\planner-{0:yyyy-MM-dd_HHmmss}-вручную" -f (Get-Date))
foreach ($database in $databases) {
    $relative = $database.Substring($DataDirectory.TrimEnd('\').Length + 1)
    $copy = Join-Path $target $relative
    New-Item -ItemType Directory -Force (Split-Path $copy -Parent) | Out-Null
    Copy-Item $database $copy
    # После аварийного завершения часть изменений может лежать в журнале -wal — копируем и его (рядом, с тем же именем)
    if (Test-Path "$database-wal") { Copy-Item "$database-wal" "$copy-wal" }
}
$keys = Join-Path $DataDirectory 'keys'
if (Test-Path $keys) { Copy-Item $keys (Join-Path $target 'keys') -Recurse }
foreach ($file in 'local.key', 'settings.json') {
    $path = Join-Path $DataDirectory $file
    if (Test-Path $path) { Copy-Item $path (Join-Path $target $file) }
}

Write-Host "Сервер не запущен — файлы скопированы: $target"
