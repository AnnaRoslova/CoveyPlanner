# Наполняет запущенный сервер демо-данными (для проверки внешнего вида).
# Запускать ТОЛЬКО против демо-базы, например:
#   dotnet run --project src/Planner.Server -- --Storage:DatabasePath=%TEMP%\planner-demo.db
#   powershell -File tools/seed-demo.ps1
param([string]$BaseUrl = 'http://localhost:5070')

$ErrorActionPreference = 'Stop'

function Api([string]$Method, [string]$Path, $Body = $null) {
    $params = @{ Method = $Method; Uri = "$BaseUrl/api/$Path"; ContentType = 'application/json; charset=utf-8' }
    if ($null -ne $Body) {
        $params.Body = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 5 -Compress))
    }
    Invoke-RestMethod @params
}

$board = Api GET 'board'
if (@($board.somedayTasks).Count -gt 0) {
    throw 'В базе уже есть дела — похоже, это не пустая демо-база. Остановлено.'
}

$today = Get-Date
$monday = $today.Date.AddDays(-((([int]$today.DayOfWeek) + 6) % 7))
function Day([int]$offset) { $monday.AddDays($offset).ToString('yyyy-MM-dd') }
$week = Day 0

# Лента
$inbox = ($board.categories | Where-Object { $_.isSystem }).id
$categories = [ordered]@{
    'Идеи'                = @('Сделать фотокнигу за лето', 'Научиться печь хлеб на закваске', 'Завести сад с травами на балконе')
    'Книги и фильмы'      = @('«7 навыков» — перечитать главу 3', 'Посмотреть «Интерстеллар» ещё раз', 'Толстой, «Детство»')
    'Покупки'             = @('Новые кроссовки', 'Органайзер для кухни')
    'Когда-нибудь поехать' = @('Байкал зимой', 'Камчатка', 'Португалия, побережье')
}
foreach ($title in @('Разобрать почту', 'Позвонить в страховую')) {
    Api POST 'tasks' @{ title = $title; target = @{ kind = 'someday'; categoryId = $inbox; index = 0 } } | Out-Null
}
foreach ($name in $categories.Keys) {
    $category = Api POST 'categories' @{ name = $name }
    $i = 0
    foreach ($title in $categories[$name]) {
        Api POST 'tasks' @{ title = $title; target = @{ kind = 'someday'; categoryId = $category.id; index = $i } } | Out-Null
        $i++
    }
    if ($name -eq 'Покупки') { Api PUT "categories/$($category.id)" @{ isExpanded = $false } | Out-Null }
}

# Роли и цели
$roles = @{}
foreach ($role in $board.roles) { $roles[$role.name] = $role.id }

Api POST 'goals' @{ roleId = $roles['Тело']; weekStart = $week; title = 'Бег 3 раза' } | Out-Null
Api POST 'goals' @{ roleId = $roles['Ум']; weekStart = $week; title = 'Прочитать 100 страниц' } | Out-Null
Api POST 'goals' @{ roleId = $roles['Общение и эмоции']; weekStart = $week; title = 'Встретиться с Олей' } | Out-Null

$g1 = Api POST 'goals' @{ roleId = $roles['Я сама']; weekStart = $week; title = 'Записаться к стоматологу' }
Api POST 'goals' @{ roleId = $roles['Семья']; weekStart = $week; title = 'Поход в парк с детьми' } | Out-Null
$g2 = Api POST 'goals' @{ roleId = $roles['Работа']; weekStart = $week; title = 'Код-ревью модуля оплаты' }
Api POST 'goals' @{ roleId = $roles['Работа']; weekStart = $week; title = 'Подготовить демо для команды' } | Out-Null
Api POST 'goals' @{ roleId = $roles['Дом']; weekStart = $week; title = 'Поменять фильтр в воде' } | Out-Null

Api POST "goals/$($g2.id)/schedule" @{ kind = 'grid'; date = (Day 1); start = '10:00:00'; durationMinutes = 120 } | Out-Null
Api POST "goals/$($g1.id)/schedule" @{ kind = 'priorities'; date = (Day 2); index = 0 } | Out-Null

# План недели
$p1 = Api POST 'tasks' @{ title = 'Купить подарок маме'; target = @{ kind = 'priorities'; date = (Day 0); index = 0 } }
Api POST 'tasks' @{ title = 'Оплатить интернет'; target = @{ kind = 'priorities'; date = (Day 0); index = 1 } } | Out-Null
Api POST 'tasks' @{ title = 'Написать отчёт'; target = @{ kind = 'priorities'; date = (Day 3); index = 0 } } | Out-Null
Api PUT "tasks/$($p1.id)/done" @{ isDone = $true } | Out-Null

$m1 = Api POST 'tasks' @{ title = 'Стендап'; target = @{ kind = 'grid'; date = (Day 0); start = '10:00:00'; durationMinutes = 30 } }
Api PUT "tasks/$($m1.id)" @{ title = 'Стендап'; notes = $null; roleId = $roles['Работа']; quadrant = 1 } | Out-Null
$m2 = Api POST 'tasks' @{ title = 'Бег в парке'; target = @{ kind = 'grid'; date = (Day 0); start = '07:30:00'; durationMinutes = 60 } }
Api PUT "tasks/$($m2.id)" @{ title = 'Бег в парке'; notes = $null; roleId = $roles['Тело']; quadrant = 2 } | Out-Null
Api POST 'tasks' @{ title = 'Созвон с заказчиком'; target = @{ kind = 'grid'; date = (Day 1); start = '11:00:00'; durationMinutes = 60 } } | Out-Null
$m3 = Api POST 'tasks' @{ title = 'Ужин с семьёй'; target = @{ kind = 'grid'; date = (Day 4); start = '19:00:00'; durationMinutes = 90 } }
Api PUT "tasks/$($m3.id)" @{ title = 'Ужин с семьёй'; notes = $null; roleId = $roles['Семья']; quadrant = 2 } | Out-Null

Write-Host 'Демо-данные добавлены.'
