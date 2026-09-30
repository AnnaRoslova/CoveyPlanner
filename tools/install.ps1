# Установка CoveyPlanner для каждодневной работы (и обновление — тот же скрипт).
#
#   powershell -File tools/install.ps1
#
# Что делает:
#  1. Публикует сервер в Release в %LOCALAPPDATA%\CoveyPlanner\app (отдельно от кода).
#     Сначала во временную папку: если сборка не удалась, работающая версия остаётся как была.
#  2. Если приложение уже запущено — делает резервную копию базы, останавливает его и заменяет файлы.
#  3. Регистрирует задачу «CoveyPlanner» в Планировщике заданий: сервер запускается при входе в систему,
#     в фоне, без окна; если он упал — перезапускается (задача повторяется каждые 5 минут и
#     запускает сервер, если он не работает).
#  4. Запускает сервер и открывает http://localhost:5070.
#
# База и резервные копии остаются где были: %LOCALAPPDATA%\CoveyPlanner\planner.db и \backups.
# Права администратора не нужны. Удалить задачу и программу (данные остаются) — tools/uninstall.ps1.
param(
    [int]$Port = 5070,
    [string]$TaskName = 'CoveyPlanner',
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'CoveyPlanner\app'),
    # Другой файл базы (по умолчанию — %LOCALAPPDATA%\CoveyPlanner\planner.db, как у dotnet run)
    [string]$DatabasePath = '',
    [switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$staging = "$InstallDir-new"
$url = "http://localhost:$Port"
$exe = Join-Path $InstallDir 'Planner.Server.exe'

function Test-Server {
    try { Invoke-WebRequest "$url/api/ping" -UseBasicParsing -TimeoutSec 2 | Out-Null; return $true } catch { return $false }
}

# 1. Публикация во временную папку
Write-Host 'Публикую (Release)...'
Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish (Join-Path $root 'src/Planner.Server') -c Release -o $staging -p:PlannerNoConsole=true --nologo -v q
if ($LASTEXITCODE -ne 0) {
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
    throw 'Публикация не удалась — установленная версия не тронута.'
}

# 2. Уже работает — резервная копия и остановка
$task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if (Test-Server) {
    try {
        $backup = Invoke-RestMethod -Method Post "$url/api/backup" -TimeoutSec 60
        Write-Host "Резервная копия перед обновлением: $($backup.path)"
    }
    catch {
        Write-Host "На $url отвечает не CoveyPlanner или старая версия без копий — копию не сделала." -ForegroundColor Yellow
    }
}
if ($task) {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
}
# Процессы из папки установки (запущенные задачей или вручную)
Get-Process Planner.Server -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($InstallDir, [StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object { Stop-Process -Id $_.Id -Force; $_.WaitForExit(5000) | Out-Null }

if (Test-Server) {
    Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
    throw "Порт $Port занят другим сервером (dotnet run или демо?). Остановите его и запустите установку снова."
}

# 3. Замена файлов
if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }
Move-Item $staging $InstallDir

# 4. Задача в Планировщике заданий
$arguments = "--urls $url"
if ($DatabasePath) { $arguments += " --Storage:DatabasePath=`"$DatabasePath`"" }
$user = "$env:USERDOMAIN\$env:USERNAME"

$action = New-ScheduledTaskAction -Execute $exe -Argument $arguments -WorkingDirectory $InstallDir
$triggers = @(
    # Запуск при входе в систему
    (New-ScheduledTaskTrigger -AtLogOn -User $user),
    # «Сторож»: каждые 5 минут, начиная с этой минуты. Если сервер работает, запуск пропускается (IgnoreNew),
    # если упал — поднимается снова. (Повтор у триггера входа в систему не подходит: он включается
    # только после входа, и сразу после установки сторожа бы не было.)
    (New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 5))
)
$settings = New-ScheduledTaskSettingsSet `
    -MultipleInstances IgnoreNew `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $TaskName -Force `
    -Description "CoveyPlanner: локальный сервер планировщика на $url (tools/install.ps1)" `
    -Action $action -Trigger $triggers -Settings $settings -Principal $principal | Out-Null

# 5. Запуск
Start-ScheduledTask -TaskName $TaskName
$ready = $false
for ($i = 0; $i -lt 60 -and -not $ready; $i++) {
    Start-Sleep -Milliseconds 500
    $ready = Test-Server
}
if (-not $ready) {
    throw "Сервер не ответил на $url. Журнал ошибок — «Просмотр событий» → Журналы Windows → Приложение."
}

Write-Host ''
Write-Host "Готово: CoveyPlanner работает на $url" -ForegroundColor Green
Write-Host "Программа: $InstallDir"
Write-Host "Задача: Планировщик заданий → «$TaskName» (запуск при входе в систему)"
Write-Host 'Обновить: снова powershell -File tools/install.ps1. Удалить: powershell -File tools/uninstall.ps1'
if (-not $NoBrowser) { Start-Process $url }
