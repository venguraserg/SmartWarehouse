<#
.SYNOPSIS
    Удаление служб SmartWarehouse, задания резервного копирования и правила брандмауэра.
    Папка C:\SmartWarehouse (настройки, резервные копии) и база данных НЕ удаляются.
    Дополнительная установка: uninstall.ps1 -Instance pr4 -WebPort 8104
#>
param([string]$Instance = "", [int]$WebPort = 80)

$ErrorActionPreference = "Continue"
$suffix = if ($Instance) { "-$Instance" } else { "" }
$titleSuffix = if ($Instance) { " ($Instance)" } else { "" }
foreach ($name in "SmartWarehouse.Web$suffix", "SmartWarehouse.Api$suffix") {
    if (Get-Service -Name $name -ErrorAction SilentlyContinue) {
        Stop-Service $name -Force
        & sc.exe delete $name | Out-Null
        Write-Host "Удалена служба $name"
    }
}
Unregister-ScheduledTask -TaskName "SmartWarehouse Backup$suffix" -Confirm:$false -ErrorAction SilentlyContinue
Remove-NetFirewallRule -DisplayName "SmartWarehouse Web$titleSuffix ($WebPort)" -ErrorAction SilentlyContinue
Write-Host "Готово. Данные и настройки остались в C:\SmartWarehouse$suffix и в PostgreSQL."
