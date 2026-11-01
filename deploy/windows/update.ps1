<#
.SYNOPSIS
    Обновление SmartWarehouse на сервере: свежий код из GitHub (ветка main) → сборка → перезапуск служб.
    Перед обновлением делается резервная копия базы. Изменения базы (миграции) применяются при запуске API.

    Запускать от имени администратора из папки репозитория:
        powershell -ExecutionPolicy Bypass -File deploy\windows\update.ps1
#>
param(
    [string]$InstallDir = "C:\SmartWarehouse",
    [string]$Branch = "main"
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")

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
Stop-Service "SmartWarehouse.Web", "SmartWarehouse.Api" -Force

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
    Start-Service "SmartWarehouse.Api", "SmartWarehouse.Web"
}

Write-Host "Готово." -ForegroundColor Green
