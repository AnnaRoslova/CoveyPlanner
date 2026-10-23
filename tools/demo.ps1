# Демо с нуля: собрать, удалить демо-данные, запустить сервер на них, наполнить демо-данными и открыть браузер.
# Запуск (из корня репозитория, в PowerShell):
#   powershell -File tools/demo.ps1
# Остановить — Ctrl+C в этом окне.
#
# Сервер запускается из копии сборки во временной папке, поэтому запущенное демо
# не мешает пересобирать проект (dotnet build / dotnet test).
param(
    [int]$Port = 5080, # 5070 занят установленным приложением (tools/install.ps1)
    [switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$data = Join-Path $env:TEMP 'planner-demo-data'
$app = Join-Path $env:TEMP 'planner-demo-app'
$url = "http://localhost:$Port"

# 1. Остановить предыдущее демо (только процессы, запущенные на демо-базе)
Get-CimInstance Win32_Process -Filter "Name = 'Planner.Server.exe'" |
    Where-Object { $_.CommandLine -match 'planner-demo' } |
    ForEach-Object {
        Write-Host "Останавливаю прежний демо-сервер (PID $($_.ProcessId))"
        Stop-Process -Id $_.ProcessId -Force
    }
Start-Sleep -Milliseconds 500

# 2. Собрать и скопировать сборку во временную папку
Write-Host 'Сборка...'
dotnet build (Join-Path $root 'src/Planner.Server') --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Сборка не удалась.' }
$bin = Join-Path $root 'src/Planner.Server/bin/Debug/net10.0'
robocopy $bin $app /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Не удалось скопировать сборку в $app" }

# 3. Удалить демо-данные (папку данных целиком; старая демо-база — файл planner-demo.db — тоже)
Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:TEMP 'planner-demo.db*') -Force -ErrorAction SilentlyContinue
Write-Host "Демо-данные: $data"

# 4. Запустить сервер в этом же окне (его журнал виден здесь)
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = $url
$server = Start-Process (Join-Path $app 'Planner.Server.exe') `
    -ArgumentList "--Storage:DataDirectory=`"$data`" --Storage:DailyBackup=false --Accounts:AdminEmail=demo@localhost" `
    -WorkingDirectory $app -NoNewWindow -PassThru

try {
    # 5. Дождаться запуска
    $ready = $false
    for ($i = 0; $i -lt 60 -and -not $ready; $i++) {
        Start-Sleep -Milliseconds 500
        try {
            Invoke-WebRequest "$url/api/ping" -UseBasicParsing -TimeoutSec 2 | Out-Null
            $ready = $true
        } catch { }
        if ($server.HasExited) { throw 'Сервер завершился при запуске — смотрите журнал выше.' }
    }
    if (-not $ready) { throw 'Сервер не ответил за 30 секунд.' }

    # 6. Демо-данные
    & (Join-Path $PSScriptRoot 'seed-demo.ps1') -BaseUrl $url

    if (-not $NoBrowser) { Start-Process $url }
    Write-Host ''
    Write-Host "Демо работает: $url   (Ctrl+C — остановить)"
    Wait-Process -Id $server.Id
}
finally {
    if (-not $server.HasExited) {
        Stop-Process -Id $server.Id -Force
        Write-Host 'Демо-сервер остановлен.'
    }
}
