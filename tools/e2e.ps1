# Проверки в настоящем браузере (Edge без окна, puppeteer-core): лента, неделя, роли, раскладка, перетаскивание.
# Запуск из корня репозитория (в PowerShell):
#   powershell -File tools/e2e.ps1              — все сценарии
#   powershell -File tools/e2e.ps1 -Only dnd    — только сценарии, в имени которых есть «dnd»
#   powershell -File tools/e2e.ps1 -AppDir "$env:LOCALAPPDATA\CoveyPlanner\app"  — против опубликованной (Release) сборки
# Нужен Node.js (зависимости ставятся сами при первом запуске). Другой браузер (Chrome):
#   $env:PLANNER_BROWSER = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
#
# Для каждого сценария — чистая временная база и свой сервер на порту 5071
# (демо на 5070 и ваша основная база не затрагиваются). Снимки при ошибках — в tests/e2e/out.
param(
    [string]$Only = '',
    [switch]$NoBuild,
    # Папка опубликованной сборки (dotnet publish): проверять её в окружении Production вместо Debug-сборки
    [string]$AppDir = ''
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8 # вывод node — в UTF-8
$root = Split-Path $PSScriptRoot -Parent
$e2e = Join-Path $root 'tests/e2e'
$out = Join-Path $e2e 'out'
$port = 5071
$url = "http://localhost:$port"
if ($AppDir) {
    $exe = Join-Path $AppDir 'Planner.Server.exe'
    $workDir = $AppDir
    $environment = 'Production'
    $NoBuild = $true
}
else {
    $exe = Join-Path $root 'src/Planner.Server/bin/Debug/net10.0/Planner.Server.exe'
    $workDir = Join-Path $root 'src/Planner.Server'
    $environment = 'Development'
}
if (-not (Test-Path $exe)) { throw "Нет сервера: $exe" }

if (-not (Get-Command node -ErrorAction SilentlyContinue)) { throw 'Нужен Node.js (https://nodejs.org).' }
if (-not (Test-Path (Join-Path $e2e 'node_modules'))) {
    Write-Host 'Устанавливаю зависимости проверок...'
    Push-Location $e2e; npm install --silent; Pop-Location
}
if (-not $NoBuild) {
    Write-Host 'Сборка...'
    dotnet build (Join-Path $root 'src/Planner.Server') --nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Сборка не удалась: dotnet build src/Planner.Server' }
}
New-Item -ItemType Directory -Force $out | Out-Null

$scripts = Get-ChildItem $e2e -Filter '*.mjs' | Where-Object { $_.BaseName -like "*$Only*" } | Sort-Object Name
$failed = 0
$summary = @()

foreach ($script in $scripts) {
    # У каждого сценария своя чистая база (файл прежней мог быть ещё занят только что остановленным сервером)
    $db = Join-Path $env:TEMP "planner-e2e-$($script.BaseName).db"
    Remove-Item "$db*" -Force -ErrorAction SilentlyContinue
    $env:ASPNETCORE_ENVIRONMENT = $environment
    $env:ASPNETCORE_URLS = $url
    $server = Start-Process $exe -ArgumentList "--Storage:DatabasePath=`"$db`" --Storage:DailyBackup=false" `
        -WorkingDirectory $workDir -WindowStyle Hidden -PassThru
    try {
        $ready = $false
        for ($i = 0; $i -lt 60 -and -not $ready; $i++) {
            Start-Sleep -Milliseconds 500
            try { Invoke-WebRequest "$url/api/ping" -UseBasicParsing -TimeoutSec 2 | Out-Null; $ready = $true } catch { }
        }
        if (-not $ready) { throw "Сервер не запустился для $($script.Name)" }

        if ((Get-Content $script.FullName -TotalCount 1) -match '@seed') {
            & (Join-Path $PSScriptRoot 'seed-demo.ps1') -BaseUrl $url *> $null
        }

        $env:PLANNER_URL = "$url/"
        Push-Location $e2e
        # Сообщения node в stderr — не повод обрывать весь прогон
        $ErrorActionPreference = 'Continue'
        $lines = node $script.Name $out 2>&1 | ForEach-Object { "$_" }
        $exitCode = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
        Pop-Location

        $ok = @($lines | Where-Object { $_ -clike 'OK *' }).Count
        $fails = @($lines | Where-Object { $_ -clike 'FAIL *' })
        if ($exitCode -ne 0 -and $fails.Count -eq 0) {
            # Сценарий упал целиком (например, страница не загрузилась)
            $fails = @("FAIL сценарий упал: $(($lines | Select-Object -Last 3) -join ' ')")
        }
        $failed += $fails.Count
        $summary += '{0,-16} OK {1,3}   FAIL {2}' -f $script.BaseName, $ok, $fails.Count
        $fails | ForEach-Object { $summary += "    $_" }
    }
    finally {
        Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
        $server.WaitForExit(5000) | Out-Null
        Remove-Item "$db*" -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ''
$summary | ForEach-Object { Write-Host $_ }
Write-Host ''
if ($failed -gt 0) {
    Write-Host "Не прошло проверок: $failed (снимки — в tests/e2e/out)" -ForegroundColor Red
    exit 1
}
Write-Host 'Все проверки прошли.' -ForegroundColor Green
