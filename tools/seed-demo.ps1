# Наполняет запущенный сервер демо-данными (как в docs/design-mockup.html) — для проверки внешнего вида.
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
if (@($board.categories).Count -gt 0 -or @($board.somedayTasks).Count -gt 0) {
    throw 'В базе уже есть категории или дела — похоже, это не пустая демо-база. Остановлено.'
}

$today = Get-Date
$monday = $today.Date.AddDays(-((([int]$today.DayOfWeek) + 6) % 7))
function Day([int]$offset) { $monday.AddDays($offset).ToString('yyyy-MM-dd') }
$week = Day 0

$roles = @{}
foreach ($role in $board.roles) { $roles[$role.name] = $role.id }

# Дело в дне; время и роль — через редактирование
function DayTask([string]$Title, [int]$Day, [string]$Time = $null, [string]$Role = $null, [bool]$Done = $false) {
    $task = Api POST 'tasks' @{ title = $Title; target = @{ kind = 'day'; date = (Day $Day); index = 99 } }
    if ($Time -or $Role) {
        $roleId = if ($Role) { $roles[$Role] } else { $null }
        $startTime = if ($Time) { "$($Time):00" } else { $null }
        Api PUT "tasks/$($task.id)" @{ title = $Title; notes = $null; roleId = $roleId; quadrant = $null; startTime = $startTime } | Out-Null
    }
    if ($Done) { Api PUT "tasks/$($task.id)/done" @{ isDone = $true } | Out-Null }
    $task
}

# Лента
$feed = [ordered]@{
    '💡|Идеи'                 = @('Сделать фотокнигу за лето', 'Научиться печь хлеб на закваске', 'Сад с травами на балконе')
    '📚|Книги и фильмы'       = @('«7 навыков» — перечитать главу 3', '«Интерстеллар» ещё раз', 'Толстой, «Детство»')
    '🏡|Дом'                  = @('Помыть окна', 'Органайзер для кухни', 'Перебрать зимние вещи', 'Починить кран')
    '✈️|Когда-нибудь поехать' = @('Байкал зимой', 'Камчатка', 'Португалия, побережье')
}
$collapsed = @('Дом', 'Когда-нибудь поехать')
foreach ($key in $feed.Keys) {
    $emoji, $name = $key -split '\|', 2
    $category = Api POST 'categories' @{ name = $name; emoji = $emoji }
    $i = 0
    foreach ($title in $feed[$key]) {
        Api POST 'tasks' @{ title = $title; target = @{ kind = 'someday'; categoryId = $category.id; index = $i } } | Out-Null
        $i++
    }
    if ($collapsed -contains $name) { Api PUT "categories/$($category.id)" @{ isExpanded = $false } | Out-Null }
}

# Цели недели
function Goal([string]$Role, [string]$Title) { Api POST 'goals' @{ roleId = $roles[$Role]; weekStart = $week; title = $Title } }
function Schedule($Goal, [int]$Day) { (Api POST "goals/$($Goal.id)/schedule" @{ kind = 'day'; date = (Day $Day); index = 99 }).task }

$run = Goal 'Тело' 'Бег ×3'
Api PUT "goals/$($run.id)" @{ isDone = $true } | Out-Null
Goal 'Ум' '100 страниц' | Out-Null
Goal 'Общение и эмоции' 'Увидеться с Олей' | Out-Null
$dentist = Goal 'Я сама' 'Записаться к стоматологу'
$park = Goal 'Семья' 'Поход в парк с детьми'
$review = Goal 'Работа' 'Код-ревью модуля оплаты'
$demo = Goal 'Работа' 'Демо для команды'
Goal 'Дом' 'Поменять фильтр для воды' | Out-Null

# План недели
DayTask 'Бег в парке' 0 '07:30' 'Тело' $true | Out-Null
DayTask 'Купить подарок маме' 0 $null 'Семья' $true | Out-Null
DayTask 'Оплатить интернет' 0 | Out-Null

$reviewTask = Schedule $review 1
Api PUT "tasks/$($reviewTask.id)" @{ title = $reviewTask.title; notes = $null; roleId = $roles['Работа']; quadrant = $null; startTime = '10:00:00' } | Out-Null
Api PUT "goals/$($review.id)" @{ isDone = $true } | Out-Null
DayTask 'Созвон с заказчиком' 1 '11:00' 'Работа' | Out-Null
DayTask 'Почитать 30 страниц' 1 $null 'Ум' | Out-Null

Schedule $dentist 2 | Out-Null

$demoTask = Schedule $demo 3
Api PUT "tasks/$($demoTask.id)" @{ title = $demoTask.title; notes = $null; roleId = $roles['Работа']; quadrant = 2; startTime = '15:00:00' } | Out-Null
DayTask 'Написать отчёт' 3 | Out-Null

DayTask 'Ужин с семьёй' 4 '19:00' 'Семья' | Out-Null

Schedule $park 5 | Out-Null
DayTask 'Кофе с Олей' 5 $null 'Общение и эмоции' | Out-Null

Write-Host 'Демо-данные добавлены.'
