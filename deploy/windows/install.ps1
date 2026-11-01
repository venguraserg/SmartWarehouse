<#
.SYNOPSIS
    Установка SmartWarehouse на ПК с Windows как службы (без Docker).

.DESCRIPTION
    Собирает API и веб-интерфейс из этой папки репозитория, создаёт настройки, службы Windows
    (запускаются сами при включении ПК), правило брандмауэра и ежедневное резервное копирование базы.
    Повторный запуск безопасен: настройки (пароли, ключи) сохраняются, программа обновляется.

    Запускать в PowerShell от имени администратора из папки репозитория:
        powershell -ExecutionPolicy Bypass -File deploy\windows\install.ps1 -ServerAddress 192.168.1.10

.PARAMETER ServerAddress
    IP-адрес или имя этого ПК в сети предприятия (по нему открывают сайт и он попадает в QR-коды наклеек).

.PARAMETER DbPassword
    Пароль пользователя postgres, заданный при установке PostgreSQL. Если не указан — будет запрошен.
#>
param(
    [Parameter(Mandatory = $true)][string]$ServerAddress,
    [string]$DbPassword,
    [string]$InstallDir = "C:\SmartWarehouse",
    [int]$WebPort = 80,
    [int]$ApiPort = 5113,
    [string]$DbName = "smartwarehouse",
    [string]$BackupTime = "02:00",
    [int]$KeepBackupDays = 30
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")

function Step($text) { Write-Host ""; Write-Host "==> $text" -ForegroundColor Cyan }

# ---------- Проверки ----------
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw "Запустите PowerShell от имени администратора (правой кнопкой — «Запуск от имени администратора»)." }

Step "Проверка .NET SDK"
$sdks = & dotnet --list-sdks 2>$null
if (-not ($sdks | Where-Object { $_ -match '^10\.' })) {
    throw "Не найден .NET 10 SDK. Установите его: https://dotnet.microsoft.com/download/dotnet/10.0 (раздел SDK, Windows x64) и откройте PowerShell заново."
}

Step "Проверка PostgreSQL"
$pgBin = Get-ChildItem "C:\Program Files\PostgreSQL\*\bin\pg_dump.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $pgBin) { throw "PostgreSQL не найден в C:\Program Files\PostgreSQL. Установите его (шаг 3 инструкции)." }
$pgService = Get-Service -Name "postgresql*" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($pgService -and $pgService.Status -ne "Running") { Start-Service $pgService.Name }

# ---------- Папки ----------
$apiDir = Join-Path $InstallDir "api"
$webDir = Join-Path $InstallDir "web"
$keysDir = Join-Path $InstallDir "keys"
$backupDir = Join-Path $InstallDir "backups"
$scriptsDir = Join-Path $InstallDir "scripts"
$logsDir = Join-Path $InstallDir "logs"
foreach ($d in @($InstallDir, $apiDir, $webDir, $keysDir, $backupDir, $scriptsDir, $logsDir)) {
    New-Item -ItemType Directory -Force -Path $d | Out-Null
}

# ---------- Настройки (создаются один раз; при повторном запуске сохраняются) ----------
$apiConfig = Join-Path $apiDir "appsettings.Production.json"
$webConfig = Join-Path $webDir "appsettings.Production.json"

if (-not (Test-Path $apiConfig)) {
    Step "Настройки API"
    if (-not $DbPassword) {
        $secure = Read-Host "Пароль пользователя postgres (задан при установке PostgreSQL)" -AsSecureString
        $DbPassword = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
    }
    # Случайный ключ подписи входа (64 символа).
    $bytes = New-Object byte[] 48
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $jwtKey = [Convert]::ToBase64String($bytes)

    $api = [ordered]@{
        Urls              = "http://localhost:$ApiPort"
        ConnectionStrings = @{ Default = "Host=localhost;Port=5432;Database=$DbName;Username=postgres;Password=$DbPassword" }
        Database          = @{ MigrateOnStartup = $true }
        Auth              = @{ JwtKey = $jwtKey; InitialAdminPassword = "admin123" }
        Logging           = @{ LogLevel = @{ Default = "Information"; "Microsoft.AspNetCore" = "Warning" } }
    }
    $api | ConvertTo-Json -Depth 5 | Set-Content -Path $apiConfig -Encoding UTF8
}
else {
    Write-Host "Настройки API уже есть — сохраняются: $apiConfig"
}

$publicUrl = if ($WebPort -eq 80) { "http://$ServerAddress" } else { "http://${ServerAddress}:$WebPort" }
if (-not (Test-Path $webConfig)) {
    Step "Настройки веб-интерфейса"
    $web = [ordered]@{
        Urls           = "http://0.0.0.0:$WebPort"
        Api            = @{ BaseUrl = "http://localhost:$ApiPort" }
        DataProtection = @{ KeysPath = $keysDir }
        App            = @{ PublicUrl = $publicUrl }
    }
    $web | ConvertTo-Json -Depth 5 | Set-Content -Path $webConfig -Encoding UTF8
}
else {
    Write-Host "Настройки веб-интерфейса уже есть — сохраняются: $webConfig"
}

# ---------- Сборка и установка программы ----------
$services = @(
    @{ Name = "SmartWarehouse.Api"; Title = "SmartWarehouse — API склада"; Exe = Join-Path $apiDir "SmartWarehouse.API.exe" },
    @{ Name = "SmartWarehouse.Web"; Title = "SmartWarehouse — веб-интерфейс склада"; Exe = Join-Path $webDir "SmartWarehouse.Web.exe" }
)

Step "Остановка служб (если уже установлены)"
foreach ($s in $services) {
    $existing = Get-Service -Name $s.Name -ErrorAction SilentlyContinue
    if ($existing -and $existing.Status -ne "Stopped") { Stop-Service $s.Name -Force }
}

Step "Сборка API"
& dotnet publish (Join-Path $repo "SmartWarehouse.API\SmartWarehouse.API.csproj") -c Release -r win-x64 --self-contained false -o $apiDir
if ($LASTEXITCODE -ne 0) { throw "Сборка API не удалась." }

Step "Сборка веб-интерфейса"
& dotnet publish (Join-Path $repo "SmartWarehouse.Web\SmartWarehouse.Web.csproj") -c Release -r win-x64 --self-contained false -o $webDir
if ($LASTEXITCODE -ne 0) { throw "Сборка веб-интерфейса не удалась." }

# ---------- Службы ----------
Step "Службы Windows"
foreach ($s in $services) {
    if (-not (Get-Service -Name $s.Name -ErrorAction SilentlyContinue)) {
        New-Service -Name $s.Name -DisplayName $s.Title -BinaryPathName "`"$($s.Exe)`"" -StartupType Automatic | Out-Null
        Write-Host "Создана служба $($s.Name)"
    }
    # При сбое — перезапуск через 10 секунд.
    & sc.exe failure $s.Name reset= 86400 actions= restart/10000/restart/10000/restart/60000 | Out-Null
}
# Веб-интерфейс стартует после API, API — после PostgreSQL.
if ($pgService) { & sc.exe config "SmartWarehouse.Api" depend= $pgService.Name | Out-Null }
& sc.exe config "SmartWarehouse.Web" depend= "SmartWarehouse.Api" | Out-Null

foreach ($s in $services) { Start-Service $s.Name }

# ---------- Брандмауэр ----------
Step "Брандмауэр: входящие на порт $WebPort"
$ruleName = "SmartWarehouse Web ($WebPort)"
if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort $WebPort -Action Allow -Profile Any | Out-Null
}

# ---------- Резервное копирование ----------
Step "Ежедневная резервная копия базы в $BackupTime"
Copy-Item (Join-Path $PSScriptRoot "backup.ps1") $scriptsDir -Force
$action = New-ScheduledTaskAction -Execute "powershell.exe" `
    -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$scriptsDir\backup.ps1`" -InstallDir `"$InstallDir`" -KeepDays $KeepBackupDays"
$trigger = New-ScheduledTaskTrigger -Daily -At $BackupTime
$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 2)
Register-ScheduledTask -TaskName "SmartWarehouse Backup" -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null

# ---------- Проверка ----------
Step "Проверка"
$ok = $false
for ($i = 0; $i -lt 30 -and -not $ok; $i++) {
    Start-Sleep -Seconds 2
    try {
        $r = Invoke-WebRequest -Uri "http://localhost:$WebPort/login" -UseBasicParsing -TimeoutSec 5
        $ok = $r.StatusCode -eq 200
    }
    catch { }
}

Write-Host ""
if ($ok) {
    Write-Host "Готово. Сайт: $publicUrl  (первый вход: admin / admin123 — сразу смените пароль)" -ForegroundColor Green
}
else {
    Write-Host "Службы установлены, но сайт пока не отвечает. Посмотрите журнал: Просмотр событий → Журналы Windows → Приложение (источники SmartWarehouse.*)." -ForegroundColor Yellow
}
Write-Host "Настройки: $apiConfig, $webConfig"
Write-Host "Резервные копии: $backupDir"
