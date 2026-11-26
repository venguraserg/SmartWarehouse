<#
.SYNOPSIS
    Откат SmartWarehouse к версии, которая работала до последнего обновления (папка C:\SmartWarehouse\previous,
    её делает update.ps1). Базу по умолчанию не трогает: новые таблицы и поля прежней версии не мешают.

    Запускать от имени администратора:
        powershell -ExecutionPolicy Bypass -File deploy\windows\rollback.ps1

    Если и прежняя версия не запускается (изменения базы ей не подходят) — откат вместе с базой
    к копии, сделанной перед обновлением. ВСЁ, что внесли в программу после обновления, пропадёт:
        powershell -ExecutionPolicy Bypass -File deploy\windows\rollback.ps1 -RestoreDatabase
    Другая копия базы:  ... -RestoreDatabase -BackupFile "C:\SmartWarehouse\backups\<файл>.backup"
    Без вопроса подтверждения:  ... -Force

    После отката следующий update.ps1 снова поставит свежую версию из main — сначала исправьте причину.
#>
param(
    [string]$Instance = "",
    [string]$InstallDir,
    [switch]$RestoreDatabase,
    [string]$BackupFile,
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$suffix = if ($Instance) { "-$Instance" } else { "" }
if (-not $InstallDir) { $InstallDir = "C:\SmartWarehouse$suffix" }
$apiService = "SmartWarehouse.Api$suffix"
$webService = "SmartWarehouse.Web$suffix"
$apiDir = Join-Path $InstallDir "api"
$webDir = Join-Path $InstallDir "web"
$previousDir = Join-Path $InstallDir "previous"
. (Join-Path $PSScriptRoot "health.ps1")

$infoFile = Join-Path $previousDir "info.json"
if (-not (Test-Path $infoFile)) { throw "Нет прежней версии ($previousDir) — откатывать не из чего." }
$info = Get-Content $infoFile -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $BackupFile) { $BackupFile = $info.Backup }

Write-Host "==> Откат: версия $($info.UpdatedTo) → $($info.Commit) (обновление $($info.Date))" -ForegroundColor Cyan
if ($RestoreDatabase) {
    if (-not (Test-Path $BackupFile)) { throw "Не найдена копия базы: $BackupFile" }
    Write-Host "    База будет восстановлена из копии: $BackupFile" -ForegroundColor Yellow
    Write-Host "    Всё, что внесли после этой копии, пропадёт." -ForegroundColor Yellow
}
if (-not $Force) {
    $answer = Read-Host "Откатить? Введите ОТКАТ"
    if ($answer -cne "ОТКАТ") { Write-Host "Отменено." -ForegroundColor Yellow; return }
}

Write-Host "==> Остановка служб" -ForegroundColor Cyan
Stop-Service $webService, $apiService -Force
try {
    Write-Host "==> Прежняя версия программы" -ForegroundColor Cyan
    Restore-Program $previousDir $apiDir $webDir

    if ($RestoreDatabase) {
        # Копия текущего состояния — на случай, если откат базы окажется лишним.
        Write-Host "==> Копия текущей базы перед восстановлением" -ForegroundColor Cyan
        & (Join-Path $PSScriptRoot "backup.ps1") -InstallDir $InstallDir

        $db = Get-DbConnection $apiDir
        Write-Host "==> Восстановление базы $($db.Database)" -ForegroundColor Cyan
        $env:PGPASSWORD = $db.Password
        try {
            & (Get-PgTool "pg_restore") -h $db.Host -p $db.Port -U $db.User -d $db.Database --clean --if-exists --no-owner $BackupFile
            if ($LASTEXITCODE -ne 0) { Write-Host "    pg_restore сообщил об ошибках (код $LASTEXITCODE) — проверьте работу программы." -ForegroundColor Yellow }
        }
        finally {
            Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
        }
    }
}
finally {
    Write-Host "==> Запуск служб" -ForegroundColor Cyan
    Start-Service $apiService, $webService
}

Write-Host "==> Проверка" -ForegroundColor Cyan
# У прежней версии может не быть /health — достаточно, что служба отвечает.
$ok = (Wait-Health (Get-HealthUrl $apiDir) 90 -AnyAnswer) -and (Wait-Health (Get-HealthUrl $webDir) 60 -AnyAnswer)
if ($ok) {
    Set-Content (Join-Path $InstallDir "version.txt") "$($info.Commit) $(Get-Date -Format 'yyyy-MM-dd HH:mm') (откат)" -Encoding UTF8
    Write-Host "Готово: работает прежняя версия $($info.Commit)." -ForegroundColor Green
}
elseif (-not $RestoreDatabase) {
    Write-Host "Прежняя версия тоже не отвечает. Попробуйте откат вместе с базой:" -ForegroundColor Red
    Write-Host "    powershell -ExecutionPolicy Bypass -File deploy\windows\rollback.ps1$(if ($Instance) { " -Instance $Instance" }) -RestoreDatabase" -ForegroundColor Red
    exit 1
}
else {
    Write-Host "Не отвечает и после восстановления базы. Просмотр событий → Приложение; пароль базы в api\appsettings.Production.json." -ForegroundColor Red
    exit 1
}
