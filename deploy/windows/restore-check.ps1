<#
.SYNOPSIS
    Проверка, что резервная копия действительно восстанавливается. Рабочая база НЕ трогается:
    копия разворачивается во временную базу <база>_restore_check, считаются строки в главных таблицах
    (рядом — те же числа в рабочей базе), временная база удаляется.

    Запускать от имени администратора (раз в месяц и после крупных обновлений):
        powershell -ExecutionPolicy Bypass -File deploy\windows\restore-check.ps1
    Конкретная копия:  ... restore-check.ps1 -BackupFile "C:\SmartWarehouse\backups\<файл>.backup"
#>
param(
    [string]$Instance = "",
    [string]$InstallDir,
    [string]$BackupFile
)

$ErrorActionPreference = "Stop"
$suffix = if ($Instance) { "-$Instance" } else { "" }
if (-not $InstallDir) { $InstallDir = "C:\SmartWarehouse$suffix" }
. (Join-Path $PSScriptRoot "health.ps1")

if (-not $BackupFile) {
    $latest = Get-ChildItem (Join-Path $InstallDir "backups") -Filter "*.backup" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $latest) { throw "Резервных копий нет в $InstallDir\backups." }
    $BackupFile = $latest.FullName
}
$db = Get-DbConnection (Join-Path $InstallDir "api")
$check = "$($db.Database)_restore_check"
$psql = Get-PgTool "psql"
$tables = @("users", "employees", "items", "batches", "stock_balances", "stock_documents", "stock_movements", "storage_locations", "item_notes", "inventories")

# Число строк; таблицы нет (копия старой версии) — «—». query_to_xml вызывается, только если таблица есть.
function Get-RowCount([string]$database, [string]$table) {
    $sql = "SELECT CASE WHEN to_regclass('public.$table') IS NULL THEN '—' ELSE (xpath('/row/c/text()', query_to_xml('SELECT count(*) AS c FROM public.$table', false, true, '')))[1]::text END;"
    $out = & $psql -h $db.Host -p $db.Port -U $db.User -d $database -t -A -c $sql
    if ($LASTEXITCODE -ne 0) { return "?" }
    return "$out".Trim()
}

Write-Host "==> Копия: $BackupFile ($([math]::Round((Get-Item $BackupFile).Length / 1KB)) КБ, $((Get-Item $BackupFile).LastWriteTime.ToString('yyyy-MM-dd HH:mm')))" -ForegroundColor Cyan
$env:PGPASSWORD = $db.Password
try {
    & $psql -h $db.Host -p $db.Port -U $db.User -d postgres -v ON_ERROR_STOP=1 -q -c "DROP DATABASE IF EXISTS `"$check`" WITH (FORCE);"
    & $psql -h $db.Host -p $db.Port -U $db.User -d postgres -v ON_ERROR_STOP=1 -q -c "CREATE DATABASE `"$check`";"
    if ($LASTEXITCODE -ne 0) { throw "Не удалось создать временную базу $check." }

    Write-Host "==> Восстановление во временную базу $check" -ForegroundColor Cyan
    & (Get-PgTool "pg_restore") -h $db.Host -p $db.Port -U $db.User -d $check --no-owner $BackupFile
    $restoreCode = $LASTEXITCODE

    Write-Host ""
    Write-Host ("{0,-20} {1,12} {2,12}" -f "Таблица", "В копии", "Сейчас")
    $empty = $true
    foreach ($table in $tables) {
        $inCopy = Get-RowCount $check $table
        if ($inCopy -notin @("—", "0")) { $empty = $false }
        Write-Host ("{0,-20} {1,12} {2,12}" -f $table, $inCopy, (Get-RowCount $db.Database $table))
    }
    Write-Host ""
    if ($restoreCode -ne 0) {
        Write-Host "pg_restore завершился с кодом $restoreCode — копия восстановилась с ошибками. Проверьте вывод выше." -ForegroundColor Red
    }
    elseif ($empty) {
        Write-Host "Копия восстановилась, но таблицы пустые — это копия пустой базы?" -ForegroundColor Yellow
    }
    else {
        Write-Host "Копия восстанавливается. Числа «В копии» — на момент копии, «Сейчас» могут быть больше." -ForegroundColor Green
    }
}
finally {
    & $psql -h $db.Host -p $db.Port -U $db.User -d postgres -q -c "DROP DATABASE IF EXISTS `"$check`" WITH (FORCE);"
    Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
}
