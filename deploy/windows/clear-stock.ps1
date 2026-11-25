<#
.SYNOPSIS
    Очистка складских данных перед повторной загрузкой начальных остатков.
    Пользователи (логины, пароли, роли), сотрудники, склады, единицы, категории и настройки сохраняются.

    Удаляются: номенклатура, фото и заметки, партии и остатки, выданное «в работу», документы и движения,
    акты и отчёты, инвентаризации, запросы на выдачу, расхождения приёмки, места хранения и оборудование.
    Нумерация документов начинается заново.

    Перед очисткой делается резервная копия базы (backup.ps1). Восстановление — см. начало backup.ps1.

    Запускать от имени администратора:
        powershell -ExecutionPolicy Bypass -File deploy\windows\clear-stock.ps1
    Без вопроса подтверждения:  ... clear-stock.ps1 -Force
#>
param(
    [string]$Instance = "",
    [string]$InstallDir,
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$suffix = if ($Instance) { "-$Instance" } else { "" }
if (-not $InstallDir) { $InstallDir = "C:\SmartWarehouse$suffix" }
$apiService = "SmartWarehouse.Api$suffix"
$webService = "SmartWarehouse.Web$suffix"

# Параметры подключения — из настроек API.
$config = Get-Content (Join-Path $InstallDir "api\appsettings.Production.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$parts = @{}
foreach ($pair in $config.ConnectionStrings.Default.Split(';')) {
    $kv = $pair.Split('=', 2)
    if ($kv.Count -eq 2) { $parts[$kv[0].Trim().ToLowerInvariant()] = $kv[1].Trim() }
}
$psql = Get-ChildItem "C:\Program Files\PostgreSQL\*\bin\psql.exe" | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $psql) { throw "psql не найден." }
$port = if ($parts["port"]) { $parts["port"] } else { "5432" }

function Sql([string]$query) {
    $out = & $psql.FullName -h $parts["host"] -p $port -U $parts["username"] -d $parts["database"] -v ON_ERROR_STOP=1 -t -A -c $query
    if ($LASTEXITCODE -ne 0) { throw "Ошибка SQL: $query" }
    return $out
}

# Что удаляется — и что остаётся.
$cleared = @(
    "item_images", "item_notes", "items", "batches", "stock_balances", "employee_balances",
    "stock_documents", "stock_movements", "receipt_discrepancies",
    "write_off_act_lines", "write_off_acts", "issue_request_lines", "issue_requests",
    "inventory_lines", "inventory_places", "inventories", "storage_locations", "equipment"
)

$env:PGPASSWORD = $parts["password"]
try {
    Write-Host "==> База $($parts['database'])" -ForegroundColor Cyan
    Write-Host "    Будет удалено:"
    foreach ($table in @("items", "batches", "stock_documents", "write_off_acts", "issue_requests", "storage_locations", "equipment")) {
        Write-Host ("      {0,-20} {1}" -f $table, (Sql "SELECT count(*) FROM $table;"))
    }
    Write-Host "    Сохраняется:"
    foreach ($table in @("users", "employees", "warehouses", "units", "categories", "app_settings")) {
        Write-Host ("      {0,-20} {1}" -f $table, (Sql "SELECT count(*) FROM $table;"))
    }

    if (-not $Force) {
        $answer = Read-Host "Очистить складские данные? Введите ОЧИСТИТЬ"
        if ($answer -cne "ОЧИСТИТЬ") { Write-Host "Отменено." -ForegroundColor Yellow; return }
    }

    Write-Host "==> Резервная копия" -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot "backup.ps1") -InstallDir $InstallDir
    # backup.ps1 в конце убирает пароль из окружения — задаём снова, иначе psql спросит пароль и не подключится.
    $env:PGPASSWORD = $parts["password"]

    Write-Host "==> Остановка служб" -ForegroundColor Cyan
    Stop-Service $webService, $apiService -Force
    try {
        Write-Host "==> Очистка" -ForegroundColor Cyan
        # Без CASCADE: если на эти таблицы ссылается что-то сохраняемое, PostgreSQL откажет, а не удалит лишнее.
        # SQL — через файл: Windows PowerShell при передаче в psql теряет кавычки внутри -c ("Key" → Key).
        $sqlFile = Join-Path $env:TEMP "smartwarehouse-clear-stock.sql"
        $sql = "BEGIN;`nTRUNCATE " + ($cleared -join ", ") + " RESTART IDENTITY;`n" +
               "DELETE FROM app_settings WHERE ""Key"" = 'main-mol';`nCOMMIT;`n"
        [System.IO.File]::WriteAllText($sqlFile, $sql, (New-Object System.Text.UTF8Encoding $false))
        try {
            & $psql.FullName -h $parts["host"] -p $port -U $parts["username"] -d $parts["database"] -v ON_ERROR_STOP=1 -q -f $sqlFile
            if ($LASTEXITCODE -ne 0) { throw "Очистка не выполнена (ошибка SQL выше) — данные не изменены." }
        }
        finally {
            Remove-Item $sqlFile -ErrorAction SilentlyContinue
        }
        Write-Host "    Готово." -ForegroundColor Green
    }
    finally {
        Write-Host "==> Запуск служб" -ForegroundColor Cyan
        Start-Service $apiService, $webService
    }
}
finally {
    Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Дальше: проверьте, что у кладовщика (Дорофей С.М.) есть учётная запись с ролью «Кладовщик»," -ForegroundColor Cyan
Write-Host "и загрузите «Начальные остатки — готово» в «Импорт»." -ForegroundColor Cyan
