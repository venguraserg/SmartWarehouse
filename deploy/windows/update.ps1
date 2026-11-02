<#
.SYNOPSIS
    Обновление SmartWarehouse на сервере: свежий код из GitHub (ветка main) → сборка → перезапуск служб.
    Перед обновлением делается резервная копия базы. Изменения базы (миграции) применяются при запуске API.

    Запускать от имени администратора из папки репозитория:
        powershell -ExecutionPolicy Bypass -File deploy\windows\update.ps1
    Другая ветка (например, следующий этап для показа):
        powershell -ExecutionPolicy Bypass -File deploy\windows\update.ps1 -Branch state-pr6-stock
    Дополнительная установка — с её именем:  ... update.ps1 -Instance pr4 -Branch state-pr4
    Начать с пустой базы (все данные удаляются; перед этим делается резервная копия):  ... -ResetDatabase
#>
param(
    [string]$Instance = "",
    [string]$InstallDir,
    [string]$Branch = "main",
    [switch]$ResetDatabase
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$suffix = if ($Instance) { "-$Instance" } else { "" }
if (-not $InstallDir) { $InstallDir = "C:\SmartWarehouse$suffix" }
$apiService = "SmartWarehouse.Api$suffix"
$webService = "SmartWarehouse.Web$suffix"

Write-Host "==> Резервная копия перед обновлением" -ForegroundColor Cyan
& (Join-Path $PSScriptRoot "backup.ps1") -InstallDir $InstallDir

Write-Host "==> Получение кода ($Branch)" -ForegroundColor Cyan
Push-Location $repo
try {
    & git fetch origin
    & git checkout $Branch
    & git pull --ff-only origin $Branch
    if ($LASTEXITCODE -ne 0) { throw "git pull не удался (есть локальные изменения?)." }
}
finally {
    Pop-Location
}

$apiDir = Join-Path $InstallDir "api"
$webDir = Join-Path $InstallDir "web"

Write-Host "==> Остановка служб" -ForegroundColor Cyan
Stop-Service $webService, $apiService -Force

if ($ResetDatabase) {
    # Параметры подключения — из настроек API; база создаётся заново при запуске API.
    $config = Get-Content (Join-Path $apiDir "appsettings.Production.json") -Raw -Encoding UTF8 | ConvertFrom-Json
    $parts = @{}
    foreach ($pair in $config.ConnectionStrings.Default.Split(';')) {
        $kv = $pair.Split('=', 2)
        if ($kv.Count -eq 2) { $parts[$kv[0].Trim().ToLowerInvariant()] = $kv[1].Trim() }
    }
    $psql = Get-ChildItem "C:\Program Files\PostgreSQL\*\bin\psql.exe" | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $psql) { throw "psql не найден." }
    Write-Host "==> Удаление базы $($parts['database']) (копия сохранена в $InstallDir\backups)" -ForegroundColor Yellow
    $env:PGPASSWORD = $parts["password"]
    try {
        & $psql.FullName -h $parts["host"] -U $parts["username"] -d postgres -v ON_ERROR_STOP=1 -c "DROP DATABASE IF EXISTS `"$($parts['database'])`" WITH (FORCE);"
        if ($LASTEXITCODE -ne 0) { throw "Не удалось удалить базу." }
    }
    finally {
        Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
    }
}

try {
    Write-Host "==> Сборка" -ForegroundColor Cyan
    & dotnet publish (Join-Path $repo "SmartWarehouse.API\SmartWarehouse.API.csproj") -c Release -r win-x64 --self-contained false -o $apiDir
    if ($LASTEXITCODE -ne 0) { throw "Сборка API не удалась." }
    & dotnet publish (Join-Path $repo "SmartWarehouse.Web\SmartWarehouse.Web.csproj") -c Release -r win-x64 --self-contained false -o $webDir
    if ($LASTEXITCODE -ne 0) { throw "Сборка веб-интерфейса не удалась." }
}
finally {
    # Службы запускаются в любом случае — при неудачной сборке работает прежняя версия.
    Write-Host "==> Запуск служб" -ForegroundColor Cyan
    Start-Service $apiService, $webService
}

Write-Host "Готово." -ForegroundColor Green
