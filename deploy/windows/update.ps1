<#
.SYNOPSIS
    Обновление SmartWarehouse на сервере: свежий код из GitHub (ветка main) → сборка → перезапуск служб → проверка.

    Порядок:
      1. Резервная копия базы (backup.ps1). Не удалась — обновление не начинается.
      2. Код из GitHub.
      3. Текущая программа копируется в C:\SmartWarehouse\previous — из неё работает откат (rollback.ps1).
      4. Сборка, запуск служб. Изменения базы (миграции) применяются при запуске API.
      5. Проверка: API отвечает «ok» (база доступна, миграции применены), веб-интерфейс отвечает.
         Не ответили — программа сама возвращается к прежней версии (база не трогается) и пишет, что делать.

    Запускать от имени администратора из папки репозитория:
        powershell -ExecutionPolicy Bypass -File deploy\windows\update.ps1
    Другая ветка (например, следующий этап для показа):
        powershell -ExecutionPolicy Bypass -File deploy\windows\update.ps1 -Branch state-pr6-stock
    Дополнительная установка — с её именем:  ... update.ps1 -Instance pr4 -Branch state-pr4
    Начать с пустой базы (все данные удаляются; перед этим делается резервная копия):  ... -ResetDatabase
    Без автоматического отката при неудачной проверке:  ... -NoAutoRollback
#>
param(
    [string]$Instance = "",
    [string]$InstallDir,
    [string]$Branch = "main",
    [switch]$ResetDatabase,
    [switch]$NoAutoRollback
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$suffix = if ($Instance) { "-$Instance" } else { "" }
if (-not $InstallDir) { $InstallDir = "C:\SmartWarehouse$suffix" }
$apiService = "SmartWarehouse.Api$suffix"
$webService = "SmartWarehouse.Web$suffix"
$apiDir = Join-Path $InstallDir "api"
$webDir = Join-Path $InstallDir "web"
$previousDir = Join-Path $InstallDir "previous"
. (Join-Path $PSScriptRoot "health.ps1")

Write-Host "==> Резервная копия перед обновлением" -ForegroundColor Cyan
& (Join-Path $PSScriptRoot "backup.ps1") -InstallDir $InstallDir
$backupFile = Get-ChildItem (Join-Path $InstallDir "backups") -Filter "*.backup" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host "    $($backupFile.FullName)"

Write-Host "==> Получение кода ($Branch)" -ForegroundColor Cyan
Push-Location $repo
try {
    $oldCommit = (& git rev-parse --short HEAD).Trim()
    & git fetch origin
    & git checkout $Branch
    & git pull --ff-only origin $Branch
    if ($LASTEXITCODE -ne 0) { throw "git pull не удался (есть локальные изменения?)." }
    $newCommit = (& git rev-parse --short HEAD).Trim()
}
finally {
    Pop-Location
}
Write-Host "    $oldCommit → $newCommit"

Write-Host "==> Остановка служб" -ForegroundColor Cyan
Stop-Service $webService, $apiService -Force

# Прежняя версия программы — для отката. Настройки (appsettings.Production.json) копируются вместе с ней.
Write-Host "==> Копия текущей программы → $previousDir" -ForegroundColor Cyan
if (Test-Path $previousDir) { Remove-Item $previousDir -Recurse -Force }
New-Item -ItemType Directory -Path $previousDir | Out-Null
Copy-Item $apiDir (Join-Path $previousDir "api") -Recurse
Copy-Item $webDir (Join-Path $previousDir "web") -Recurse
[ordered]@{
    Commit = $oldCommit
    UpdatedTo = $newCommit
    Backup = $backupFile.FullName
    Date = (Get-Date -Format "yyyy-MM-dd HH:mm")
} | ConvertTo-Json | Set-Content (Join-Path $previousDir "info.json") -Encoding UTF8

if ($ResetDatabase) {
    # Параметры подключения — из настроек API; база создаётся заново при запуске API.
    $db = Get-DbConnection $apiDir
    Write-Host "==> Удаление базы $($db.Database) (копия: $($backupFile.Name))" -ForegroundColor Yellow
    $env:PGPASSWORD = $db.Password
    try {
        & (Get-PgTool "psql") -h $db.Host -p $db.Port -U $db.User -d postgres -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS `"$($db.Database)`" WITH (FORCE);"
        if ($LASTEXITCODE -ne 0) { throw "Не удалось удалить базу." }
    }
    finally {
        Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
    }
}

$built = $false
try {
    Write-Host "==> Сборка" -ForegroundColor Cyan
    & dotnet publish (Join-Path $repo "SmartWarehouse.API\SmartWarehouse.API.csproj") -c Release -r win-x64 --self-contained false -o $apiDir
    if ($LASTEXITCODE -ne 0) { throw "Сборка API не удалась." }
    & dotnet publish (Join-Path $repo "SmartWarehouse.Web\SmartWarehouse.Web.csproj") -c Release -r win-x64 --self-contained false -o $webDir
    if ($LASTEXITCODE -ne 0) { throw "Сборка веб-интерфейса не удалась." }
    $built = $true
}
finally {
    if (-not $built) {
        # Сборка могла перезаписать часть файлов — возвращаем прежнюю программу целиком.
        Write-Host "==> Сборка не удалась — возвращаю прежнюю версию программы" -ForegroundColor Yellow
        Restore-Program $previousDir $apiDir $webDir
    }
    Write-Host "==> Запуск служб" -ForegroundColor Cyan
    Start-Service $apiService, $webService
}

Write-Host "==> Проверка" -ForegroundColor Cyan
$apiOk = Wait-Health (Get-HealthUrl $apiDir) 120
$webOk = $apiOk -and (Wait-Health (Get-HealthUrl $webDir) 60)
if ($apiOk -and $webOk) {
    Set-Content (Join-Path $InstallDir "version.txt") "$newCommit $(Get-Date -Format 'yyyy-MM-dd HH:mm')" -Encoding UTF8
    Write-Host "Готово: версия $newCommit работает." -ForegroundColor Green
    return
}

Write-Host ""
Write-Host "Новая версия не отвечает: $(if (-not $apiOk) { 'API (база недоступна или изменения базы не применились)' } else { 'веб-интерфейс' })." -ForegroundColor Red
Write-Host "Подробности: Просмотр событий → Журналы Windows → Приложение (источник SmartWarehouse.*)." -ForegroundColor Red
if ($NoAutoRollback) {
    Write-Host "Откат: powershell -ExecutionPolicy Bypass -File deploy\windows\rollback.ps1$(if ($Instance) { " -Instance $Instance" })" -ForegroundColor Yellow
    exit 1
}
Write-Host "==> Автоматический откат к версии $oldCommit (база не трогается)" -ForegroundColor Yellow
& (Join-Path $PSScriptRoot "rollback.ps1") -Instance $Instance -InstallDir $InstallDir -Force
exit 1
