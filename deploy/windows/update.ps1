<#
.SYNOPSIS
    Обновление SmartWarehouse на сервере: свежий код из GitHub (ветка main) → сборка → перезапуск служб.
    Перед обновлением делается резервная копия базы. Изменения базы (миграции) применяются при запуске API.

    Запускать от имени администратора из папки репозитория:
        powershell -ExecutionPolicy Bypass -File deploy\windows\update.ps1
    Дополнительная установка (этап для показа) — с её именем и веткой:
        powershell -ExecutionPolicy Bypass -File deploy\windows\update.ps1 -Instance pr4 -Branch state-pr4
#>
param(
    [string]$Instance = "",
    [string]$InstallDir,
    [string]$Branch = "main"
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
