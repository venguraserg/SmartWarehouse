<#
.SYNOPSIS
    Резервная копия базы SmartWarehouse (pg_dump). Запускается планировщиком каждый день (см. install.ps1),
    можно запустить и вручную от имени администратора.

    Восстановление из копии (все данные базы заменяются данными копии!):
        Stop-Service SmartWarehouse.Web, SmartWarehouse.Api
        & "C:\Program Files\PostgreSQL\17\bin\pg_restore.exe" -U postgres -h localhost -d smartwarehouse --clean --if-exists "C:\SmartWarehouse\backups\<файл>.backup"
        Start-Service SmartWarehouse.Api, SmartWarehouse.Web
#>
param(
    [string]$InstallDir = "C:\SmartWarehouse",
    [int]$KeepDays = 30
)

$ErrorActionPreference = "Stop"
$backupDir = Join-Path $InstallDir "backups"
$log = Join-Path $InstallDir "logs\backup.log"
New-Item -ItemType Directory -Force -Path $backupDir, (Split-Path $log) | Out-Null

function Log($text) { "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $text" | Add-Content -Path $log -Encoding UTF8 }

try {
    # Параметры подключения — из настроек API.
    $config = Get-Content (Join-Path $InstallDir "api\appsettings.Production.json") -Raw -Encoding UTF8 | ConvertFrom-Json
    $parts = @{}
    foreach ($pair in $config.ConnectionStrings.Default.Split(';')) {
        $kv = $pair.Split('=', 2)
        if ($kv.Count -eq 2) { $parts[$kv[0].Trim().ToLowerInvariant()] = $kv[1].Trim() }
    }

    $pgDump = Get-ChildItem "C:\Program Files\PostgreSQL\*\bin\pg_dump.exe" | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $pgDump) { throw "pg_dump не найден." }

    $file = Join-Path $backupDir ("{0}_{1}.backup" -f $parts["database"], (Get-Date -Format "yyyy-MM-dd_HHmm"))
    $env:PGPASSWORD = $parts["password"]
    & $pgDump.FullName -h $parts["host"] -p $(if ($parts["port"]) { $parts["port"] } else { "5432" }) -U $parts["username"] -F c -f $file $parts["database"]
    if ($LASTEXITCODE -ne 0) { throw "pg_dump завершился с ошибкой $LASTEXITCODE." }
    Log "Копия создана: $file ($([math]::Round((Get-Item $file).Length / 1KB)) КБ)"

    # Старые копии удаляются.
    Get-ChildItem $backupDir -Filter "*.backup" | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-$KeepDays) } |
        ForEach-Object { Remove-Item $_.FullName; Log "Удалена старая копия: $($_.Name)" }
}
catch {
    Log "ОШИБКА: $($_.Exception.Message)"
    throw
}
finally {
    Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
}
